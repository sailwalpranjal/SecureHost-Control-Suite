# SecureHost Control Suite - Architecture & Design

## Summary

SecureHost Control Suite is an host security platform providing tamper-resistant, kernel-enforced policy control over network connections, device access, and application resource usage on Windows systems.

**Version**: 1.0.0
**Author**: Pranjal Sailwal
**Last Updated**: 2025-12-15

---

## Table of Contents

1. [System Architecture](#system-architecture)
2. [Component Design](#component-design)
3. [Security Architecture](#security-architecture)
4. [Data Flow](#data-flow)
5. [API Design](#api-design)
6. [Deployment Architecture](#deployment-architecture)
7. [Performance Considerations](#performance-considerations)
8. [Scalability & Reliability](#scalability--reliability)

---

## 1. System Architecture

### 1.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Management Layer (User Mode)                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐           │
│  │   GUI Client │  │  CLI Client  │  │  REST API    │           │
│  │   (WPF/C#)   │  │    (C#)      │  │  (HTTP/IPC)  │           │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘           │
│         │                 │                  │                  │
│         └─────────────────┴──────────────────┘                  │
│                            │                                    │
│                            ▼                                    │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │         SecureHost Service (Windows Service/SYSTEM)      │   │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────┐            │   │
│  │  │   Policy   │ │   Audit    │ │   Auth     │            │   │
│  │  │   Engine   │ │   Engine   │ │  Manager   │            │   │
│  │  └────────────┘ └────────────┘ └────────────┘            │   │
│  └──────────────────────────┬───────────────────────────────┘   │
└─────────────────────────────┼───────────────────────────────────┘
                              │ IOCTL/DeviceControl
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Kernel Mode (Ring 0)                          │
│  ┌──────────────────────┐  ┌──────────────────────┐             │
│  │  SecureHostWFP.sys   │  │ SecureHostDevice.sys │             │
│  │  (WFP Callout)       │  │  (Device Filter)     │             │
│  │                      │  │                      │             │
│  │  • IPv4/IPv6 filter  │  │  • Camera control    │             │ 
│  │  • Per-process ACL   │  │  • Microphone block  │             │
│  │  • Port blocking     │  │  • USB filtering     │             │
│  │  • Audit logging     │  │  • Bluetooth block   │             │
│  └──────────────────────┘  └──────────────────────┘             │
│         │                          │                            │
│         ▼                          ▼                            │
│  ┌────────────────┐  ┌────────────────────────────┐             │
│  │ Windows Filter │  │   PnP Manager / Device     │             │
│  │ Platform (WFP) │  │   Installation Framework   │             │
│  └────────────────┘  └────────────────────────────┘             │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 Component Layers

#### Layer 1: Kernel Mode Enforcement
- **Purpose**: Tamper-proof enforcement at the lowest privilege level
- **Components**:
  - WFP callout driver (network)
  - Device filter driver (hardware)
- **Trust Level**: Ring 0 (kernel)
- **Attack Surface**: Minimal

#### Layer 2: System Service (SYSTEM)
- **Purpose**: Policy management and orchestration
- **Components**:
  - Policy engine
  - Audit engine
  - Driver communication layer
  - REST API server
- **Trust Level**: SYSTEM account with protected process
- **Language**: C# .NET 8.0 (memory-safe)

#### Layer 3: Management Clients
- **Purpose**: User interface and automation
- **Components**:
  - WPF GUI (Windows desktop)
  - CLI (command-line and scripting)
  - REST API clients
- **Trust Level**: User context
- **Authentication**: Named pipes with ACL or localhost HTTPS

---

## 2. Component Design

### 2.1 Kernel Drivers

#### 2.1.1 SecureHostWFP.sys (Network Filter)

**Technology**: Windows Filtering Platform (WFP) Callout Driver

**Responsibilities**:
- Intercept network connection attempts (TCP/UDP)
- Enforce per-process, per-port, per-protocol rules
- Log blocked and allowed connections
- Support IPv4 and IPv6

**Architecture**:
```c
DriverEntry()
  ├─> Register WFP callouts (IPv4 + IPv6)
  ├─> Add sublayer for SecureHost filters
  └─> Open shared memory for user-mode communication

ClassifyFn(packet, metadata)
  ├─> Extract: PID, protocol, local_port, remote_port, remote_IP
  ├─> Query policy (cached or via shared memory)
  ├─> Decision: FWP_ACTION_PERMIT / FWP_ACTION_BLOCK
  └─> Log event to ETW

NotifyFn(filter_add/delete)
  └─> Update internal filter state

FlowDeleteFn(flow_context)
  └─> Clean up flow-specific state
```

**Performance**:
- Lock-free rule lookup using RCU-like pattern
- Pre-computed hash tables for process+port combinations
- Zero-copy packet inspection (metadata only)

#### 2.1.2 SecureHostDevice.sys (Device Filter)

**Technology**: KMDF Device Filter Driver

**Responsibilities**:
- Monitor device plug-and-play events
- Block access to camera, microphone, USB, Bluetooth
- Enforce application-specific device policies

**Architecture**:
```c
DriverEntry()
  ├─> Initialize KMDF framework
  ├─> Register PnP notification callbacks
  └─> Open control device for user-mode IOCTL

DeviceAdd(device_init)
  ├─> Classify device type (camera/mic/USB/BT)
  ├─> Query policy engine
  └─> Attach filter if policy requires blocking

PnPNotifyCallback(device_event)
  ├─> Device arrival → Check policy → Disable if blocked
  └─> Device removal → Update internal state

IoDeviceControl(IOCTL_CHECK_ACCESS)
  ├─> Validate calling process
  ├─> Query rule database
  └─> Return: ACCESS_GRANTED / ACCESS_DENIED
```

**Device Identification**:
- Uses device class GUIDs (GUID_DEVCLASS_CAMERA, etc.)
- Hardware ID matching (VID/PID for USB)
- Device interface enumeration

### 2.2 User-Mode Service

#### 2.2.1 SecureHostService (Windows Service)

**Framework**: .NET 8.0 Worker Service

**Lifecycle**:
```csharp
OnStart()
  ├─> Load policies from encrypted storage
  ├─> Initialize drivers (send initial rule set)
  ├─> Start audit logger (ETW + file)
  ├─> Start REST API server (localhost:5555)
  ├─> Start device/network monitors
  └─> Enter service loop (health checks)

OnStop()
  ├─> Flush audit logs
  ├─> Save policy state
  ├─> Gracefully shutdown API server
  └─> Signal drivers to unload
```

**Modules**:

| Module | Responsibility | Technology |
|--------|---------------|------------|
| PolicyEngine | Rule evaluation, CRUD operations | C# |
| AuditEngine | ETW logging, SIEM export (CEF) | C# + ETW |
| SecureStorage | Encrypted config (DPAPI) | C# + Windows DPAPI |
| DriverComm | IOCTL communication with drivers | P/Invoke |
| ApiServer | REST API (Kestrel) | ASP.NET Core Minimal API |

#### 2.2.2 Policy Engine

**Data Structure**:
```csharp
class PolicyEngine {
  ConcurrentDictionary<ulong, PolicyRule> _rules;
  ReaderWriterLockSlim _rulesLock;

  PolicyDecision EvaluateNetworkConnection(...);
  PolicyDecision EvaluateDeviceAccess(...);
  ulong AddRule(PolicyRule);
  bool UpdateRule(ulong id, PolicyRule);
  bool RemoveRule(ulong id);
}
```

**Evaluation Algorithm**:
1. Acquire read lock on rule set
2. Filter rules by type (Network/Device/Application)
3. Sort by priority (descending)
4. Iterate until first match:
   - Check process ID/name filters
   - Check resource filters (port, device type, etc.)
   - Check temporal constraints (ValidFrom/ValidUntil)
   - Check user SID filter
5. Return action (Allow/Block/Audit) + rule ID
6. Release lock

**Concurrency**:
- Reader-writer lock for rule updates (rare)
- Lock-free read path for evaluations (common)
- Rule cloning for atomicity

#### 2.2.3 Audit Engine

**Event Flow**:
```
Policy Decision
  ↓
AuditEvent Created
  ↓
ETW (Real-time) ──→ Event Viewer / Log Analytics
  ↓
Queue (In-Memory)
  ↓
Batch Write (every 5s or 100 events)
  ↓
JSON Lines File (daily rotation)
  ↓
Export to SIEM (CEF format)
```

**Event Schema** (JSON):
```json
{
  "id": "uuid",
  "timestamp": "ISO8601",
  "eventType": "NetworkConnection",
  "severity": "Warning",
  "processId": 1234,
  "processName": "chrome.exe",
  "action": "Block",
  "ruleId": 42,
  "networkDetails": {
    "protocol": "TCP",
    "localPort": 443,
    "remoteAddress": "93.184.216.34",
    "remotePort": 443
  }
}
```

**Retention**:
- Default: 90 days
- Configurable via appsettings.json

### 2.3 Management Clients

#### 2.3.1 SecureHostGUI (WPF Desktop)

**Technology**: WPF + Material Design + MVVM

**Features**:
- Dashboard (service status, rule count, connections)
- Policy rule management (CRUD)
- Network connection viewer (real-time)
- Audit log export (CEF, JSON)
- Settings editor

**Communication**: REST API over HTTP (localhost only)

**Security**:
- Named pipe authentication (Windows ACL)
- Process identity validation
- Secure storage of credentials (if needed)

#### 2.3.2 SecureHostCLI (Command-Line)

**Technology**: System.CommandLine + Spectre.Console

**Commands**:
```bash
SecureHostCLI status                    # Service health
SecureHostCLI rules list                # List all rules
SecureHostCLI rules add --name "..." --type Network --action Block
SecureHostCLI rules delete --id 42
SecureHostCLI network connections       # Active connections
SecureHostCLI network listeners         # Listening ports
SecureHostCLI audit export --start "2025-01-01" --end "2025-01-31" --output audit.cef
```

---

## 3. Security Architecture

### 3.1 Threat Model

**Assets**:
1. Policy rules (confidentiality, integrity)
2. Audit logs (integrity, availability)
3. Service availability
4. Kernel driver integrity

**Threat Actors**:
- **T1**: Malicious local user (unprivileged)
- **T2**: Malicious local admin
- **T3**: Malware/exploit running as SYSTEM
- **T4**: Kernel rootkit

**Threats** (STRIDE):

| Threat | Description | Mitigation |
|--------|-------------|------------|
| **Spoofing** | T1 impersonates admin to modify policies | Named pipe ACL, process identity validation |
| **Tampering** | T2 modifies driver or service binaries | Code signing, WHQL, Secure Boot, PatchGuard |
| **Repudiation** | T1 denies malicious action | Immutable audit trail (ETW + file) |
| **Information Disclosure** | T1 reads sensitive policy data | Encrypted storage (DPAPI), ACLs |
| **Denial of Service** | T3 crashes service/driver | Exception handling, watchdog, auto-restart |
| **Elevation of Privilege** | T1 → T2 via driver exploit | Minimal attack surface, fuzzing, PREfast |

### 3.2 Defense Layers

#### Layer 1: Code Integrity
- **Driver Signing**: WHQL-signed drivers (required for production)
- **Service Signing**: Authenticode with EV certificate
- **Secure Boot**: UEFI verification chain
- **WDAC**: Windows Defender Application Control policies

#### Layer 2: Process Protection
- **PPL (Protected Process Light)**: Service runs as PPL
- **ACLs**: Service files protected (SYSTEM + Administrators only)
- **Tamper Detection**: Periodic self-integrity checks

#### Layer 3: Data Protection
- **DPAPI**: Machine-scoped encryption for policy storage
- **HMAC**: Integrity verification for encrypted data
- **Secure Delete**: Overwrite-before-delete for sensitive files

#### Layer 4: Network Security
- **Localhost Only**: API listens on 127.0.0.1 (no external exposure)
- **Named Pipes**: Alternative IPC with Windows ACLs
- **TLS 1.3**: Optional HTTPS with self-signed cert

#### Layer 5: Audit Integrity
- **ETW (Event Tracing for Windows)**: Kernel-level logging (tamper-resistant)
- **Append-Only Logs**: File permissions prevent modification
- **SIEM Export**: Off-host archival for forensics

### 3.3 Least Privilege

| Component | Account | Privileges | Rationale |
|-----------|---------|------------|-----------|
| WFP Driver | SYSTEM | Kernel mode | Required for WFP callouts |
| Device Driver | SYSTEM | Kernel mode | Required for PnP filtering |
| Service | SYSTEM | Protected Process | Needs driver IOCTL + admin tasks |
| GUI/CLI | User | None | Read-only via API (write requires elevation) |

---

## 4. Data Flow

### 4.1 Network Connection Flow

```
Application initiates TCP connect()
  ↓
TCP/IP Stack
  ↓
WFP Layer: FWPM_LAYER_ALE_AUTH_CONNECT_V4
  ↓
SecureHostWFP.sys::ClassifyFn()
  ├─> Extract metadata (PID, ports, IP)
  ├─> Query cached policy
  ├─> Decision: PERMIT / BLOCK
  └─> Log to ETW
  ↓
[PERMIT] → Connection established
[BLOCK]  → Connection denied (RST sent)
  ↓
SecureHostService receives ETW event
  ↓
AuditEngine logs to file (JSON)
  ↓
[Optional] SIEM export (CEF)
```

### 4.2 Device Access Flow

```
Device plugged in (USB camera)
  ↓
PnP Manager detects device
  ↓
SecureHostDevice.sys::PnPNotifyCallback()
  ├─> Identify device type (Camera)
  ├─> Query policy via IOCTL
  │     ↓
  │   SecureHostService::DriverCommService
  │     ├─> PolicyEngine.EvaluateDeviceAccess(...)
  │     └─> Return: ALLOW / BLOCK
  ├─> [BLOCK] → Invoke device.Disable()
  └─> [ALLOW] → Allow driver to load
  ↓
AuditEngine logs event
```

### 4.3 Policy Update Flow

```
Admin: SecureHostCLI rules add --name "Block Chrome Camera" --type Device --action Block --process chrome.exe --device Camera
  ↓
CLI → HTTP POST /api/rules
  ↓
SecureHostService::ApiServer
  ↓
PolicyManagementService::AddRuleAsync()
  ├─> PolicyEngine.AddRule() → Assign ID
  ├─> SecureStorage.Save() → Encrypt + persist
  ├─> DriverComm.SendDeviceRule() → IOCTL to driver
  └─> AuditEngine.LogPolicyChange()
  ↓
SecureHostDevice.sys receives IOCTL
  ├─> Add rule to in-kernel rule table
  └─> ACK to user mode
  ↓
CLI displays success message
```

---

## 5. API Design

### 5.1 REST API Endpoints

**Base URL**: `http://localhost:5555/api`

#### Health & Status
```
GET /health
  Response: { "status": "healthy", "version": "1.0.0" }

GET /status
  Response: {
    "service": "running",
    "version": "1.0.0",
    "rulesCount": 42,
    "activeRules": 38,
    "machineId": "DESKTOP-ABC123",
    "uptime": "2.03:45:12"
  }
```

#### Policy Rules
```
GET /rules
  Response: [ { PolicyRule }, ... ]

GET /rules/{id}
  Response: { PolicyRule }

POST /rules
  Body: { PolicyRule }
  Response: { "id": 123, "message": "Rule added" }

PUT /rules/{id}
  Body: { PolicyRule }
  Response: { "message": "Rule updated" }

DELETE /rules/{id}
  Response: { "message": "Rule deleted" }
```

#### Network Monitoring
```
GET /network/connections
  Response: [ { "protocol": "TCP", "localAddress": "...", ... }, ... ]

GET /network/listeners
  Response: [ { "protocol": "TCP", "address": "...", "port": 443 }, ... ]
```

#### Audit Export
```
GET /audit/export?startTime=2025-01-01T00:00:00Z&endTime=2025-01-31T23:59:59Z
  Response: (CEF file download)
```

### 5.2 IPC (Named Pipes - Alternative)

**Pipe Name**: `\\.\pipe\SecureHostControl`

**Security**:
- ACL: Administrators + SYSTEM (write)
- ACL: Authenticated Users (read)

**Protocol**: JSON-RPC 2.0

**Methods**:
- `getRules()`
- `addRule(rule)`
- `updateRule(id, rule)`
- `deleteRule(id)`
- `getStatus()`

---

## 6. Deployment Architecture

### 6.1 Standalone Deployment

```
┌─────────────────────────┐
│   Windows 10/11 Client  │
│                         │
│  ┌──────────────────┐   │
│  │ SecureHost Suite │   │
│  │  - Service       │   │
│  │  - Drivers       │   │
│  │  - GUI           │   │
│  └──────────────────┘   │
│                         │
│  Policies: Local        │
│  Audit: Local Files     │
└─────────────────────────┘
```

### 6.2 Enterprise Deployment

```
┌────────────────────────────────────────────────────┐
│                  Enterprise Environment            │
│                                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐ │
│  │ Client 1    │  │ Client 2    │  │ Client N    │ │
│  │ SecureHost  │  │ SecureHost  │  │ SecureHost  │ │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘ │
│         │                │                │        │
│         └────────────────┴────────────────┘        │
│                          │                         │
│                          ▼                         │
│         ┌────────────────────────────┐             │
│         │  Management Server (SaaS)  │             │
│         │  - Centralized Policies    │             │
│         │  - Audit Aggregation       │             │
│         │  - Reporting Dashboard     │             │
│         │  - Role-Based Access       │             │
│         └────────────────────────────┘             │
│                          │                         │
│                          ▼                         │
│         ┌────────────────────────────┐             │
│         │  SIEM (Splunk/QRadar/etc.) │             │
│         └────────────────────────────┘             │
└────────────────────────────────────────────────────┘
```

### 6.3 High-Availability (Future)

- Multiple service instances with shared policy store
- Load-balanced API gateway
- Distributed audit log collection (Kafka/Event Hubs)

---

## 7. Performance Considerations

### 7.1 Benchmarks (Target)

| Operation | Target Latency | Notes |
|-----------|---------------|-------|
| Network classification (kernel) | < 5 μs | Per-packet overhead |
| Device access check (kernel) | < 10 μs | PnP callback |
| Policy evaluation (user mode) | < 100 μs | Rule lookup |
| Rule CRUD (API) | < 50 ms | Including persistence |
| Audit log write | < 1 ms | Batched async |

### 7.2 Scalability

- **Rules**: Supports up to 10,000 rules with hash-based indexing
- **Connections**: Monitors up to 100,000 concurrent connections
- **Throughput**: 1M+ packets/second (network filter)
- **Audit**: 10,000 events/second sustained

### 7.3 Resource Usage

| Component | CPU (idle) | CPU (active) | Memory | Disk I/O |
|-----------|------------|--------------|--------|----------|
| WFP Driver | < 0.1% | < 2% | 2 MB | None |
| Device Driver | < 0.1% | < 1% | 1 MB | None |
| Service | < 1% | < 5% | 50 MB | 1 MB/s (audit) |
| GUI | < 1% | < 3% | 100 MB | Minimal |

---

## 8. Scalability & Reliability

### 8.1 Fault Tolerance

- **Kernel Drivers**: Exception handling with graceful degradation
- **Service**: Watchdog + auto-restart (SCM recovery)
- **Audit**: Buffered writes with retry logic
- **Storage**: Transactional updates with rollback

### 8.2 Monitoring & Observability

- **ETW Traces**: Real-time kernel event stream
- **Performance Counters**: Custom counters for rules, connections, blocks
- **Health Checks**: `/api/health` endpoint + Windows Service status
- **Alerts**: Windows Event Log integration

### 8.3 Disaster Recovery

- **Policy Backup**: Automatic daily backups to %ProgramData%\SecureHost\Backups
- **Audit Archival**: Optional S3/Azure Blob export
- **Safe Mode**: Fallback to default "allow-all" policy if corruption detected

---

## Appendix A: Technology Stack

| Layer | Component | Technology | Version |
|-------|-----------|------------|---------|
| Kernel | WFP Driver | C, WDK | 10.0.22621.0 |
| Kernel | Device Driver | C, KMDF | 1.31 |
| Service | Runtime | .NET | 8.0 LTS |
| Service | Web Server | Kestrel | 8.0 |
| GUI | Framework | WPF + Material Design | .NET 8.0 |
| CLI | Framework | System.CommandLine + Spectre.Console | .NET 8.0 |
| Build | Compiler | MSVC 2022 + .NET SDK | Latest |
| Installer | Tool | WiX Toolset | 3.14 |
| Tests | Framework | xUnit + Moq + FluentAssertions | Latest |

---

## Appendix B: References

- Windows Filtering Platform: https://docs.microsoft.com/en-us/windows/win32/fwp/
- KMDF Driver Development: https://docs.microsoft.com/en-us/windows-hardware/drivers/wdf/
- Windows Driver Signing: https://docs.microsoft.com/en-us/windows-hardware/drivers/install/
- ETW (Event Tracing for Windows): https://docs.microsoft.com/en-us/windows/win32/etw/
- DPAPI: https://docs.microsoft.com/en-us/windows/win32/seccng/data-protection-api
- CEF (Common Event Format): https://www.microfocus.com/documentation/arcsight/arcsight-smartconnectors-8.3/pdfdoc/cef-implementation-standard/cef-implementation-standard.pdf

---

**Document Version**: 1.0
**Approval**: Pranjal Sailwal
**Classification**: Internal Use Only
