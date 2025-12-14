/*++

Module Name:
    driver.c

Abstract:
    SecureHost WFP Network Policy Enforcement Driver
    Windows Filtering Platform callout driver for network control

Environment:
    Kernel mode only

--*/

#include <ntddk.h>
#include <wdf.h>
#include <fwpsk.h>
#include <fwpmk.h>
#include <guiddef.h>
#include <initguid.h>
#include <ntstrsafe.h>

#pragma warning(push)
#pragma warning(disable:4201) // nameless struct/union

//
// Pool tags for memory allocation tracking
//
#define SECUREHOST_WFP_TAG 'FWHS'  // 'SHWF' reversed

//
// Driver version
//
#define SECUREHOST_WFP_VERSION_MAJOR 1
#define SECUREHOST_WFP_VERSION_MINOR 0

//
// Callout and sublayer GUIDs
//
// {E5F6A7B8-C9D0-8E9F-2A3B-4C5D6E7F8A9B}
DEFINE_GUID(
    SECUREHOST_WFP_CALLOUT_V4_GUID,
    0xe5f6a7b8, 0xc9d0, 0x8e9f, 0x2a, 0x3b, 0x4c, 0x5d, 0x6e, 0x7f, 0x8a, 0x9b
);

// {F6A7B8C9-D0E1-9F0A-3B4C-5D6E7F8A9B0C}
DEFINE_GUID(
    SECUREHOST_WFP_CALLOUT_V6_GUID,
    0xf6a7b8c9, 0xd0e1, 0x9f0a, 0x3b, 0x4c, 0x5d, 0x6e, 0x7f, 0x8a, 0x9b, 0x0c
);

// {A7B8C9D0-E1F2-0A1B-4C5D-6E7F8A9B0C1D}
DEFINE_GUID(
    SECUREHOST_WFP_SUBLAYER_GUID,
    0xa7b8c9d0, 0xe1f2, 0x0a1b, 0x4c, 0x5d, 0x6e, 0x7f, 0x8a, 0x9b, 0x0c, 0x1d
);

//
// Policy rule structure
//
typedef struct _SECUREHOST_POLICY_RULE {
    UINT64 RuleId;
    UINT32 ProcessId;
    UINT16 Protocol;
    UINT16 LocalPort;
    UINT16 RemotePort;
    UINT32 Action;  // FWP_ACTION_BLOCK or FWP_ACTION_PERMIT
    BOOLEAN Enabled;
} SECUREHOST_POLICY_RULE, *PSECUREHOST_POLICY_RULE;

//
// Global driver context
//
typedef struct _SECUREHOST_DRIVER_CONTEXT {
    WDFDRIVER Driver;
    HANDLE EngineHandle;
    UINT32 CalloutIdV4;
    UINT32 CalloutIdV6;
    UINT32 FilterIdV4;
    UINT32 FilterIdV6;
    KSPIN_LOCK RulesLock;
    LIST_ENTRY RulesList;
    UINT64 NextRuleId;
} SECUREHOST_DRIVER_CONTEXT, *PSECUREHOST_DRIVER_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(SECUREHOST_DRIVER_CONTEXT, GetDriverContext)

//
// Function declarations
//
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_UNLOAD SecureHostEvtDriverUnload;

NTSTATUS
SecureHostRegisterCallouts(
    _Inout_ PSECUREHOST_DRIVER_CONTEXT Context
);

NTSTATUS
SecureHostUnregisterCallouts(
    _Inout_ PSECUREHOST_DRIVER_CONTEXT Context
);

VOID NTAPI
SecureHostClassifyFn(
    _In_ const FWPS_INCOMING_VALUES0* InFixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* InMetaValues,
    _Inout_opt_ VOID* LayerData,
    _In_opt_ const void* ClassifyContext,
    _In_ const FWPS_FILTER3* Filter,
    _In_ UINT64 FlowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* ClassifyOut
);

NTSTATUS NTAPI
SecureHostNotifyFn(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE NotifyType,
    _In_ const GUID* FilterKey,
    _Inout_ FWPS_FILTER3* Filter
);

VOID NTAPI
SecureHostFlowDeleteFn(
    _In_ UINT16 LayerId,
    _In_ UINT32 CalloutId,
    _In_ UINT64 FlowContext
);

//
// Paged code segments
//
#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, SecureHostEvtDriverUnload)
#pragma alloc_text(PAGE, SecureHostRegisterCallouts)
#pragma alloc_text(PAGE, SecureHostUnregisterCallouts)
#endif

/*++

Routine Description:
    Driver entry point. Initializes WDF driver and registers WFP callouts.

--*/
_Use_decl_annotations_
NTSTATUS
DriverEntry(
    PDRIVER_OBJECT DriverObject,
    PUNICODE_STRING RegistryPath
)
{
    NTSTATUS status;
    WDF_DRIVER_CONFIG config;
    WDFDRIVER driver;
    WDF_OBJECT_ATTRIBUTES attributes;
    PSECUREHOST_DRIVER_CONTEXT context;

    KdPrint(("SecureHostWFP: DriverEntry\n"));

    //
    // Initialize WDF driver
    //
    WDF_DRIVER_CONFIG_INIT(&config, WDF_NO_EVENT_CALLBACK);
    config.DriverInitFlags = WdfDriverInitNonPnpDriver;
    config.EvtDriverUnload = SecureHostEvtDriverUnload;

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, SECUREHOST_DRIVER_CONTEXT);
    attributes.EvtCleanupCallback = NULL;

    status = WdfDriverCreate(
        DriverObject,
        RegistryPath,
        &attributes,
        &config,
        &driver
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: WdfDriverCreate failed: 0x%08X\n", status));
        return status;
    }

    //
    // Initialize driver context
    //
    context = GetDriverContext(driver);
    RtlZeroMemory(context, sizeof(SECUREHOST_DRIVER_CONTEXT));

    context->Driver = driver;
    KeInitializeSpinLock(&context->RulesLock);
    InitializeListHead(&context->RulesList);
    context->NextRuleId = 1;

    //
    // Register WFP callouts
    //
    status = SecureHostRegisterCallouts(context);
    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: SecureHostRegisterCallouts failed: 0x%08X\n", status));
        return status;
    }

    KdPrint(("SecureHostWFP: Driver loaded successfully\n"));
    return STATUS_SUCCESS;
}

/*++

Routine Description:
    Driver unload callback. Cleans up WFP callouts and resources.

--*/
_Use_decl_annotations_
VOID
SecureHostEvtDriverUnload(
    WDFDRIVER Driver
)
{
    PSECUREHOST_DRIVER_CONTEXT context;

    PAGED_CODE();

    KdPrint(("SecureHostWFP: SecureHostEvtDriverUnload\n"));

    context = GetDriverContext(Driver);

    //
    // Unregister callouts
    //
    SecureHostUnregisterCallouts(context);

    //
    // Clean up rules list
    //
    while (!IsListEmpty(&context->RulesList)) {
        PLIST_ENTRY entry = RemoveHeadList(&context->RulesList);
        PSECUREHOST_POLICY_RULE rule = CONTAINING_RECORD(
            entry,
            SECUREHOST_POLICY_RULE,
            RuleId
        );
        ExFreePoolWithTag(rule, SECUREHOST_WFP_TAG);
    }

    KdPrint(("SecureHostWFP: Driver unloaded\n"));
}

/*++

Routine Description:
    Registers WFP callouts for IPv4 and IPv6 traffic inspection.

--*/
_Use_decl_annotations_
NTSTATUS
SecureHostRegisterCallouts(
    PSECUREHOST_DRIVER_CONTEXT Context
)
{
    NTSTATUS status;
    FWPS_CALLOUT3 calloutV4 = {0};
    FWPS_CALLOUT3 calloutV6 = {0};
    FWPM_CALLOUT0 mCallout = {0};
    FWPM_SUBLAYER0 sublayer = {0};
    FWPM_FILTER0 filter = {0};
    FWPM_FILTER_CONDITION0 conditions[1] = {0};

    PAGED_CODE();

    KdPrint(("SecureHostWFP: Registering callouts\n"));

    //
    // Open filter engine
    //
    status = FwpmEngineOpen0(
        NULL,
        RPC_C_AUTHN_DEFAULT,
        NULL,
        NULL,
        &Context->EngineHandle
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpmEngineOpen0 failed: 0x%08X\n", status));
        goto cleanup;
    }

    //
    // Begin transaction
    //
    status = FwpmTransactionBegin0(Context->EngineHandle, 0);
    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpmTransactionBegin0 failed: 0x%08X\n", status));
        goto cleanup;
    }

    //
    // Register IPv4 callout
    //
    calloutV4.calloutKey = SECUREHOST_WFP_CALLOUT_V4_GUID;
    calloutV4.classifyFn = SecureHostClassifyFn;
    calloutV4.notifyFn = SecureHostNotifyFn;
    calloutV4.flowDeleteFn = SecureHostFlowDeleteFn;
    calloutV4.flags = 0;

    status = FwpsCalloutRegister3(
        (PDEVICE_OBJECT)WdfDriverWdmGetDriverObject(Context->Driver),
        &calloutV4,
        &Context->CalloutIdV4
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpsCalloutRegister3 (V4) failed: 0x%08X\n", status));
        FwpmTransactionAbort0(Context->EngineHandle);
        goto cleanup;
    }

    //
    // Register IPv6 callout
    //
    calloutV6.calloutKey = SECUREHOST_WFP_CALLOUT_V6_GUID;
    calloutV6.classifyFn = SecureHostClassifyFn;
    calloutV6.notifyFn = SecureHostNotifyFn;
    calloutV6.flowDeleteFn = SecureHostFlowDeleteFn;
    calloutV6.flags = 0;

    status = FwpsCalloutRegister3(
        (PDEVICE_OBJECT)WdfDriverWdmGetDriverObject(Context->Driver),
        &calloutV6,
        &Context->CalloutIdV6
    );

    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpsCalloutRegister3 (V6) failed: 0x%08X\n", status));
        FwpsCalloutUnregisterById0(Context->CalloutIdV4);
        FwpmTransactionAbort0(Context->EngineHandle);
        goto cleanup;
    }

    //
    // Add sublayer
    //
    sublayer.subLayerKey = SECUREHOST_WFP_SUBLAYER_GUID;
    sublayer.displayData.name = L"SecureHost WFP Sublayer";
    sublayer.displayData.description = L"SecureHost network policy enforcement";
    sublayer.weight = 0x8000; // High priority

    status = FwpmSubLayerAdd0(Context->EngineHandle, &sublayer, NULL);
    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpmSubLayerAdd0 failed: 0x%08X\n", status));
        FwpsCalloutUnregisterById0(Context->CalloutIdV4);
        FwpsCalloutUnregisterById0(Context->CalloutIdV6);
        FwpmTransactionAbort0(Context->EngineHandle);
        goto cleanup;
    }

    //
    // Add IPv4 management callout
    //
    mCallout.calloutKey = SECUREHOST_WFP_CALLOUT_V4_GUID;
    mCallout.displayData.name = L"SecureHost WFP IPv4 Callout";
    mCallout.displayData.description = L"Inspects IPv4 network traffic";
    mCallout.applicableLayer = FWPM_LAYER_ALE_AUTH_CONNECT_V4;

    status = FwpmCalloutAdd0(Context->EngineHandle, &mCallout, NULL, NULL);
    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpmCalloutAdd0 (V4) failed: 0x%08X\n", status));
        FwpsCalloutUnregisterById0(Context->CalloutIdV4);
        FwpsCalloutUnregisterById0(Context->CalloutIdV6);
        FwpmTransactionAbort0(Context->EngineHandle);
        goto cleanup;
    }

    //
    // Add IPv6 management callout
    //
    mCallout.calloutKey = SECUREHOST_WFP_CALLOUT_V6_GUID;
    mCallout.displayData.name = L"SecureHost WFP IPv6 Callout";
    mCallout.displayData.description = L"Inspects IPv6 network traffic";
    mCallout.applicableLayer = FWPM_LAYER_ALE_AUTH_CONNECT_V6;

    status = FwpmCalloutAdd0(Context->EngineHandle, &mCallout, NULL, NULL);
    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpmCalloutAdd0 (V6) failed: 0x%08X\n", status));
        FwpsCalloutUnregisterById0(Context->CalloutIdV4);
        FwpsCalloutUnregisterById0(Context->CalloutIdV6);
        FwpmTransactionAbort0(Context->EngineHandle);
        goto cleanup;
    }

    //
    // Commit transaction
    //
    status = FwpmTransactionCommit0(Context->EngineHandle);
    if (!NT_SUCCESS(status)) {
        KdPrint(("SecureHostWFP: FwpmTransactionCommit0 failed: 0x%08X\n", status));
        FwpsCalloutUnregisterById0(Context->CalloutIdV4);
        FwpsCalloutUnregisterById0(Context->CalloutIdV6);
        goto cleanup;
    }

    KdPrint(("SecureHostWFP: Callouts registered successfully\n"));
    return STATUS_SUCCESS;

cleanup:
    if (Context->EngineHandle != NULL) {
        FwpmEngineClose0(Context->EngineHandle);
        Context->EngineHandle = NULL;
    }
    return status;
}

/*++

Routine Description:
    Unregisters WFP callouts and closes filter engine.

--*/
_Use_decl_annotations_
NTSTATUS
SecureHostUnregisterCallouts(
    PSECUREHOST_DRIVER_CONTEXT Context
)
{
    PAGED_CODE();

    KdPrint(("SecureHostWFP: Unregistering callouts\n"));

    if (Context->CalloutIdV4 != 0) {
        FwpsCalloutUnregisterById0(Context->CalloutIdV4);
    }

    if (Context->CalloutIdV6 != 0) {
        FwpsCalloutUnregisterById0(Context->CalloutIdV6);
    }

    if (Context->EngineHandle != NULL) {
        FwpmEngineClose0(Context->EngineHandle);
        Context->EngineHandle = NULL;
    }

    KdPrint(("SecureHostWFP: Callouts unregistered\n"));
    return STATUS_SUCCESS;
}

/*++

Routine Description:
    WFP classify callback. Inspects network traffic and applies policy rules.

--*/
_Use_decl_annotations_
VOID NTAPI
SecureHostClassifyFn(
    const FWPS_INCOMING_VALUES0* InFixedValues,
    const FWPS_INCOMING_METADATA_VALUES0* InMetaValues,
    VOID* LayerData,
    const void* ClassifyContext,
    const FWPS_FILTER3* Filter,
    UINT64 FlowContext,
    FWPS_CLASSIFY_OUT0* ClassifyOut
)
{
    UNREFERENCED_PARAMETER(LayerData);
    UNREFERENCED_PARAMETER(ClassifyContext);
    UNREFERENCED_PARAMETER(Filter);
    UNREFERENCED_PARAMETER(FlowContext);

    UINT16 localPort = 0;
    UINT16 remotePort = 0;
    UINT32 processId = 0;
    FWP_DIRECTION direction;

    //
    // Extract connection details
    //
    if (FWPS_IS_METADATA_FIELD_PRESENT(InMetaValues, FWPS_METADATA_FIELD_PROCESS_ID)) {
        processId = (UINT32)InMetaValues->processId;
    }

    //
    // Get ports from fixed values (layer-dependent indices)
    //
    localPort = InFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_LOCAL_PORT].value.uint16;
    remotePort = InFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_REMOTE_PORT].value.uint16;
    direction = InFixedValues->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_DIRECTION].value.uint8;

    //
    // Default action: permit
    // In production, query policy engine here
    //
    ClassifyOut->actionType = FWP_ACTION_PERMIT;

    //
    // Log connection attempt (production: send to user-mode service)
    //
    KdPrint(("SecureHostWFP: Connection - PID:%lu Local:%u Remote:%u Dir:%u\n",
             processId, localPort, remotePort, direction));

    //
    // Clear rights to prevent other filters from processing
    //
    if (Filter->flags & FWPS_FILTER_FLAG_CLEAR_ACTION_RIGHT) {
        ClassifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
    }
}

/*++

Routine Description:
    WFP notify callback. Handles filter add/delete notifications.

--*/
_Use_decl_annotations_
NTSTATUS NTAPI
SecureHostNotifyFn(
    FWPS_CALLOUT_NOTIFY_TYPE NotifyType,
    const GUID* FilterKey,
    FWPS_FILTER3* Filter
)
{
    UNREFERENCED_PARAMETER(FilterKey);
    UNREFERENCED_PARAMETER(Filter);

    switch (NotifyType) {
        case FWPS_CALLOUT_NOTIFY_ADD_FILTER:
            KdPrint(("SecureHostWFP: Filter added\n"));
            break;

        case FWPS_CALLOUT_NOTIFY_DELETE_FILTER:
            KdPrint(("SecureHostWFP: Filter deleted\n"));
            break;

        default:
            break;
    }

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    WFP flow delete callback. Cleans up flow context.

--*/
_Use_decl_annotations_
VOID NTAPI
SecureHostFlowDeleteFn(
    UINT16 LayerId,
    UINT32 CalloutId,
    UINT64 FlowContext
)
{
    UNREFERENCED_PARAMETER(LayerId);
    UNREFERENCED_PARAMETER(CalloutId);
    UNREFERENCED_PARAMETER(FlowContext);

    KdPrint(("SecureHostWFP: Flow deleted\n"));
}

#pragma warning(pop)
