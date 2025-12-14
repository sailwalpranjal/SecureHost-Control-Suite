/*++

Module Name:
    driver.c

Abstract:
    SecureHost Device Access Control Filter Driver
    Controls access to camera, microphone, USB, and Bluetooth devices

Environment:
    Kernel mode only

--*/

#include <ntddk.h>
#include <wdf.h>
#include <initguid.h>
#include <devpkey.h>
#include <ntstrsafe.h>

#pragma warning(push)
#pragma warning(disable:4201)

//
// Pool tag for memory allocation
//
#define SECUREHOST_DEVICE_TAG 'VDHS'  // 'SHDV' reversed

//
// Device types we monitor
//
typedef enum _SECUREHOST_DEVICE_TYPE {
    DeviceTypeCamera = 1,
    DeviceTypeMicrophone = 2,
    DeviceTypeUSB = 3,
    DeviceTypeBluetooth = 4,
    DeviceTypeUnknown = 0
} SECUREHOST_DEVICE_TYPE;

//
// Device policy structure
//
typedef struct _SECUREHOST_DEVICE_POLICY {
    SECUREHOST_DEVICE_TYPE DeviceType;
    UINT32 ProcessId;
    BOOLEAN Allowed;
    WCHAR ProcessName[256];
} SECUREHOST_DEVICE_POLICY, *PSECUREHOST_DEVICE_POLICY;

//
// Device extension
//
typedef struct _DEVICE_CONTEXT {
    WDFDEVICE Device;
    WDFQUEUE Queue;
    SECUREHOST_DEVICE_TYPE DeviceType;
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, DeviceGetContext)

//
// Global driver context
//
typedef struct _DRIVER_CONTEXT {
    WDFDRIVER Driver;
    KSPIN_LOCK PolicyLock;
    LIST_ENTRY PolicyList;
} DRIVER_CONTEXT, *PDRIVER_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DRIVER_CONTEXT, DriverGetContext)

//
// Function declarations
//
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD SecureHostDeviceAdd;
EVT_WDF_OBJECT_CONTEXT_CLEANUP SecureHostDriverCleanup;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL SecureHostIoDeviceControl;

NTSTATUS
SecureHostCheckDeviceAccess(
    _In_ PDRIVER_CONTEXT Context,
    _In_ SECUREHOST_DEVICE_TYPE DeviceType,
    _In_ UINT32 ProcessId
);

SECUREHOST_DEVICE_TYPE
SecureHostIdentifyDevice(
    _In_ PWDFDEVICE_INIT DeviceInit
);

//
// Paged code
//
#ifdef ALLOC_PRAGMA
#pragma alloc_text (INIT, DriverEntry)
#pragma alloc_text (PAGE, SecureHostDeviceAdd)
#pragma alloc_text (PAGE, SecureHostDriverCleanup)
#endif

/*++

Routine Description:
    DriverEntry initializes the driver and creates WDF driver object.

--*/
_Use_decl_annotations_
NTSTATUS
DriverEntry(
    PDRIVER_OBJECT DriverObject,
    PUNICODE_STRING RegistryPath
)
{
    WDF_DRIVER_CONFIG config;
    NTSTATUS status;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDFDRIVER driver;
    PDRIVER_CONTEXT context;

    KdPrint(("SecureHostDevice: DriverEntry\n"));

    //
    // Initialize driver configuration
    //
    WDF_DRIVER_CONFIG_INIT(&config, SecureHostDeviceAdd);

    //
    // Create driver attributes with context
    //
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DRIVER_CONTEXT);
    attributes.EvtCleanupCallback = SecureHostDriverCleanup;

    //
    // Create WDF driver object
    //
    status = WdfDriverCreate(
        DriverObject,
        RegistryPath,
        &attributes,
        &config,
        &driver
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostDevice: WdfDriverCreate failed: 0x%08X\n", status));
        return status;
    }

    //
    // Initialize driver context
    //
    context = DriverGetContext(driver);
    RtlZeroMemory(context, sizeof(DRIVER_CONTEXT));
    context->Driver = driver;
    KeInitializeSpinLock(&context->PolicyLock);
    InitializeListHead(&context->PolicyList);

    KdPrint(("SecureHostDevice: Driver initialized successfully\n"));
    return STATUS_SUCCESS;
}

/*++

Routine Description:
    SecureHostDeviceAdd is called by the framework when a device is added.

--*/
_Use_decl_annotations_
NTSTATUS
SecureHostDeviceAdd(
    WDFDRIVER Driver,
    PWDFDEVICE_INIT DeviceInit
)
{
    NTSTATUS status;
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    PDEVICE_CONTEXT deviceContext;
    WDFDEVICE device;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDFQUEUE queue;

    UNREFERENCED_PARAMETER(Driver);

    PAGED_CODE();

    KdPrint(("SecureHostDevice: SecureHostDeviceAdd\n"));

    //
    // Set device characteristics
    //
    WdfDeviceInitSetDeviceType(DeviceInit, FILE_DEVICE_UNKNOWN);
    WdfDeviceInitSetCharacteristics(DeviceInit, FILE_DEVICE_SECURE_OPEN, TRUE);
    WdfDeviceInitSetIoType(DeviceInit, WdfDeviceIoBuffered);

    //
    // Initialize device attributes
    //
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, DEVICE_CONTEXT);

    //
    // Create device object
    //
    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostDevice: WdfDeviceCreate failed: 0x%08X\n", status));
        return status;
    }

    //
    // Initialize device context
    //
    deviceContext = DeviceGetContext(device);
    deviceContext->Device = device;
    deviceContext->DeviceType = SecureHostIdentifyDevice(DeviceInit);

    //
    // Create default I/O queue
    //
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchParallel);
    queueConfig.EvtIoDeviceControl = SecureHostIoDeviceControl;

    status = WdfIoQueueCreate(
        device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        &queue
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostDevice: WdfIoQueueCreate failed: 0x%08X\n", status));
        return status;
    }

    deviceContext->Queue = queue;

    KdPrint(("SecureHostDevice: Device added successfully\n"));
    return STATUS_SUCCESS;
}

/*++

Routine Description:
    Driver cleanup callback.

--*/
_Use_decl_annotations_
VOID
SecureHostDriverCleanup(
    WDFOBJECT DriverObject
)
{
    PDRIVER_CONTEXT context;

    PAGED_CODE();

    KdPrint(("SecureHostDevice: SecureHostDriverCleanup\n"));

    context = DriverGetContext((WDFDRIVER)DriverObject);

    //
    // Clean up policy list
    //
    while (!IsListEmpty(&context->PolicyList)) {
        PLIST_ENTRY entry = RemoveHeadList(&context->PolicyList);
        PSECUREHOST_DEVICE_POLICY policy = CONTAINING_RECORD(
            entry,
            SECUREHOST_DEVICE_POLICY,
            DeviceType
        );
        ExFreePoolWithTag(policy, SECUREHOST_DEVICE_TAG);
    }
}

/*++

Routine Description:
    Handles device I/O control requests.

--*/
_Use_decl_annotations_
VOID
SecureHostIoDeviceControl(
    WDFQUEUE Queue,
    WDFREQUEST Request,
    size_t OutputBufferLength,
    size_t InputBufferLength,
    ULONG IoControlCode
)
{
    NTSTATUS status = STATUS_SUCCESS;
    PDEVICE_CONTEXT deviceContext;
    PDRIVER_CONTEXT driverContext;
    UINT32 processId;
    HANDLE processHandle;

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    deviceContext = DeviceGetContext(WdfIoQueueGetDevice(Queue));
    driverContext = DriverGetContext(WdfGetDriver());

    //
    // Get requesting process ID
    //
    processId = HandleToUlong(PsGetCurrentProcessId());

    switch (IoControlCode) {
        case IOCTL_SECUREHOST_CHECK_ACCESS:
            //
            // Check if process has access to this device
            //
            status = SecureHostCheckDeviceAccess(
                driverContext,
                deviceContext->DeviceType,
                processId
            );

            if (!NT_SUCCESS(status)) {
                KdPrint(("SecureHostDevice: Access denied for PID %lu to device type %d\n",
                         processId, deviceContext->DeviceType));
                status = STATUS_ACCESS_DENIED;
            }
            break;

        default:
            status = STATUS_INVALID_DEVICE_REQUEST;
            break;
    }

    WdfRequestComplete(Request, status);
}

/*++

Routine Description:
    Checks if a process has access to a specific device type.

--*/
_Use_decl_annotations_
NTSTATUS
SecureHostCheckDeviceAccess(
    PDRIVER_CONTEXT Context,
    SECUREHOST_DEVICE_TYPE DeviceType,
    UINT32 ProcessId
)
{
    KIRQL oldIrql;
    PLIST_ENTRY entry;
    NTSTATUS status = STATUS_ACCESS_DENIED;

    //
    // Search policy list
    //
    KeAcquireSpinLock(&Context->PolicyLock, &oldIrql);

    for (entry = Context->PolicyList.Flink;
         entry != &Context->PolicyList;
         entry = entry->Flink) {

        PSECUREHOST_DEVICE_POLICY policy = CONTAINING_RECORD(
            entry,
            SECUREHOST_DEVICE_POLICY,
            DeviceType
        );

        if (policy->DeviceType == DeviceType &&
            (policy->ProcessId == 0 || policy->ProcessId == ProcessId)) {

            if (policy->Allowed) {
                status = STATUS_SUCCESS;
                break;
            }
        }
    }

    KeReleaseSpinLock(&Context->PolicyLock, oldIrql);

    return status;
}

/*++

Routine Description:
    Identifies the device type based on device properties.

--*/
_Use_decl_annotations_
SECUREHOST_DEVICE_TYPE
SecureHostIdentifyDevice(
    PWDFDEVICE_INIT DeviceInit
)
{
    UNREFERENCED_PARAMETER(DeviceInit);

    //
    // In production, query device properties to identify type
    // For now, return unknown
    //
    return DeviceTypeUnknown;
}

//
// IOCTL definitions
//
#define IOCTL_SECUREHOST_CHECK_ACCESS \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS)

#pragma warning(pop)
