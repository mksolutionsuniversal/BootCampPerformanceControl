#include "BootCampSmc.h"

#define BOOTCAMP_SMC_MMIO_RESPONSE_TYPE_OFFSET ((SIZE_T)0x0000u)
#define BOOTCAMP_SMC_MMIO_RESPONSE_LENGTH_OFFSET ((SIZE_T)0x0005u)
#define BOOTCAMP_SMC_MMIO_RESPONSE_ATTRIBUTES_OFFSET ((SIZE_T)0x0006u)
#define BOOTCAMP_SMC_MMIO_KEY_NAME_OFFSET ((SIZE_T)0x0078u)
#define BOOTCAMP_SMC_MMIO_SMC_ID_OFFSET ((SIZE_T)0x007Eu)
#define BOOTCAMP_SMC_MMIO_COMMAND_RESULT_OFFSET ((SIZE_T)0x007Fu)
#define BOOTCAMP_SMC_MMIO_STATUS_OFFSET ((SIZE_T)0x4005u)

#define BOOTCAMP_SMC_GATE5_MMIO_REQUIRED_LENGTH ((SIZE_T)0x4006u)

#define BOOTCAMP_SMC_COMMAND_GET_KEY_INFO ((UCHAR)0x13u)
#define BOOTCAMP_SMC_KEY_F0MX ((ULONG)0x784D3046u)
#define BOOTCAMP_SMC_KEY_F1MX ((ULONG)0x784D3146u)

#define BOOTCAMP_SMC_GET_KEY_INFO_F0MX_STATUS_COMPLETE_MASK ((UCHAR)0x20u)
#define BOOTCAMP_SMC_GET_KEY_INFO_F0MX_MAX_POLL_COUNT ((ULONG)25u)
#define BOOTCAMP_SMC_GET_KEY_INFO_F0MX_POLL_INTERVAL_100NS ((LONGLONG)-100000LL)
#define BOOTCAMP_SMC_GET_KEY_INFO_F0MX_RESULT_SUCCESS ((UCHAR)0x00u)

#define BOOTCAMP_SMC_GET_KEY_INFO_F1MX_STATUS_COMPLETE_MASK ((UCHAR)0x20u)
#define BOOTCAMP_SMC_GET_KEY_INFO_F1MX_MAX_POLL_COUNT ((ULONG)25u)
#define BOOTCAMP_SMC_GET_KEY_INFO_F1MX_POLL_INTERVAL_100NS ((LONGLONG)-100000LL)
#define BOOTCAMP_SMC_GET_KEY_INFO_F1MX_RESULT_SUCCESS ((UCHAR)0x00u)

static
NTSTATUS
BootCampSmcGate5DGetF0MxKeyInfo(
    _In_ PBOOTCAMP_SMC_DEVICE_CONTEXT context
    );

static
NTSTATUS
BootCampSmcGate5DGetF1MxKeyInfo(
    _In_ PBOOTCAMP_SMC_DEVICE_CONTEXT context
    );

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, BootCampSmcEvtDeviceAdd)
#pragma alloc_text(PAGE, BootCampSmcEvtDevicePrepareHardware)
#pragma alloc_text(PAGE, BootCampSmcEvtDeviceReleaseHardware)
#pragma alloc_text(PAGE, BootCampSmcGate5DGetF0MxKeyInfo)
#pragma alloc_text(PAGE, BootCampSmcGate5DGetF1MxKeyInfo)
#endif

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES attributes;

    WDF_DRIVER_CONFIG_INIT(&config, BootCampSmcEvtDeviceAdd);
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);

    return WdfDriverCreate(
        DriverObject,
        RegistryPath,
        &attributes,
        &config,
        WDF_NO_HANDLE);
}

NTSTATUS
BootCampSmcEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit)
{
    NTSTATUS status;
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_PNPPOWER_EVENT_CALLBACKS pnpCallbacks;

    UNREFERENCED_PARAMETER(Driver);
    PAGED_CODE();

    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnpCallbacks);
    pnpCallbacks.EvtDevicePrepareHardware = BootCampSmcEvtDevicePrepareHardware;
    pnpCallbacks.EvtDeviceReleaseHardware = BootCampSmcEvtDeviceReleaseHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(DeviceInit, &pnpCallbacks);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(
        &attributes,
        BOOTCAMP_SMC_DEVICE_CONTEXT);

    status = WdfDeviceCreate(
        &DeviceInit,
        &attributes,
        &device);

    if (!NT_SUCCESS(status))
    {
        KdPrintEx((
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: WdfDeviceCreate failed: 0x%08X\n",
            status));
        return status;
    }

    RtlZeroMemory(
        BootCampSmcGetDeviceContext(device),
        sizeof(BOOTCAMP_SMC_DEVICE_CONTEXT));

    KdPrintEx((
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: device object created; no hardware register has been accessed.\n"));

    return STATUS_SUCCESS;
}

NTSTATUS
BootCampSmcEvtDevicePrepareHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesRaw,
    _In_ WDFCMRESLIST ResourcesTranslated)
{
    NTSTATUS status;
    ULONG index;
    PBOOTCAMP_SMC_DEVICE_CONTEXT context;

    PAGED_CODE();

    context = BootCampSmcGetDeviceContext(Device);

    if (context->MmioBase != NULL)
    {
        KdPrintEx((
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: refusing PrepareHardware because an MMIO mapping is already active.\n"));
        return STATUS_INVALID_DEVICE_STATE;
    }

    RtlZeroMemory(context, sizeof(*context));

    context->RawResourceCount = WdfCmResourceListGetCount(ResourcesRaw);
    context->TranslatedResourceCount = WdfCmResourceListGetCount(ResourcesTranslated);

    KdPrintEx((
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: EvtDevicePrepareHardware raw=%lu translated=%lu.\n",
        context->RawResourceCount,
        context->TranslatedResourceCount));

    for (index = 0; index < context->TranslatedResourceCount; ++index)
    {
        PCM_PARTIAL_RESOURCE_DESCRIPTOR descriptor;

        descriptor = WdfCmResourceListGetDescriptor(
            ResourcesTranslated,
            index);

        if (descriptor == NULL)
        {
            KdPrintEx((
                DPFLTR_IHVDRIVER_ID,
                DPFLTR_ERROR_LEVEL,
                "BootCampSmc: translated resource %lu could not be retrieved.\n",
                index));
            continue;
        }

        switch (descriptor->Type)
        {
        case CmResourceTypePort:
            if (!context->HasPortResource)
            {
                context->HasPortResource = TRUE;
                context->PortStart = descriptor->u.Port.Start;
                context->PortLength = descriptor->u.Port.Length;
            }

            KdPrintEx((
                DPFLTR_IHVDRIVER_ID,
                DPFLTR_INFO_LEVEL,
                "BootCampSmc: translated PORT[%lu] start=0x%I64X length=0x%lX flags=0x%X (metadata only).\n",
                index,
                descriptor->u.Port.Start.QuadPart,
                descriptor->u.Port.Length,
                descriptor->Flags));
            break;

        case CmResourceTypeMemory:
            ++context->MemoryResourceCount;

            if (!context->HasMemoryResource)
            {
                context->HasMemoryResource = TRUE;
                context->MemoryStart = descriptor->u.Memory.Start;
                context->MemoryLength = descriptor->u.Memory.Length;
            }

            KdPrintEx((
                DPFLTR_IHVDRIVER_ID,
                DPFLTR_INFO_LEVEL,
                "BootCampSmc: translated MEMORY[%lu] start=0x%I64X length=0x%lX flags=0x%X.\n",
                index,
                descriptor->u.Memory.Start.QuadPart,
                descriptor->u.Memory.Length,
                descriptor->Flags));
            break;

        case CmResourceTypeInterrupt:
            if (!context->HasInterruptResource)
            {
                context->HasInterruptResource = TRUE;
                context->InterruptLevel = descriptor->u.Interrupt.Level;
                context->InterruptVector = descriptor->u.Interrupt.Vector;
                context->InterruptAffinity = descriptor->u.Interrupt.Affinity;
            }

            KdPrintEx((
                DPFLTR_IHVDRIVER_ID,
                DPFLTR_INFO_LEVEL,
                "BootCampSmc: translated INTERRUPT[%lu] level=%lu vector=%lu affinity=0x%I64X flags=0x%X (metadata only).\n",
                index,
                descriptor->u.Interrupt.Level,
                descriptor->u.Interrupt.Vector,
                (ULONGLONG)descriptor->u.Interrupt.Affinity,
                descriptor->Flags));
            break;

        default:
            KdPrintEx((
                DPFLTR_IHVDRIVER_ID,
                DPFLTR_INFO_LEVEL,
                "BootCampSmc: translated resource[%lu] type=%u flags=0x%X (not accessed).\n",
                index,
                descriptor->Type,
                descriptor->Flags));
            break;
        }
    }

    if (context->MemoryResourceCount != 1 ||
        !context->HasMemoryResource ||
        context->MemoryLength == 0)
    {
        KdPrintEx((
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: expected exactly one non-empty translated MEMORY resource; count=%lu length=0x%lX.\n",
            context->MemoryResourceCount,
            context->MemoryLength));
        return STATUS_DEVICE_CONFIGURATION_ERROR;
    }

    if ((SIZE_T)context->MemoryLength < BOOTCAMP_SMC_GATE5_MMIO_REQUIRED_LENGTH)
    {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: translated MEMORY resource is too small for Gate 5D-B GET_KEY_INFO(F0Mx/F1Mx); length=0x%lX required=0x%IX.\n",
            context->MemoryLength,
            BOOTCAMP_SMC_GATE5_MMIO_REQUIRED_LENGTH);
        return STATUS_DEVICE_CONFIGURATION_ERROR;
    }

    context->MmioLength = BOOTCAMP_SMC_GATE5_MMIO_REQUIRED_LENGTH;
    context->MmioBase = MmMapIoSpaceEx(
        context->MemoryStart,
        BOOTCAMP_SMC_GATE5_MMIO_REQUIRED_LENGTH,
        PAGE_READWRITE | PAGE_NOCACHE);

    if (context->MmioBase == NULL)
    {
        context->MmioLength = 0;

        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: writable Gate 5 MMIO mapping failed.\n");
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: writable non-cached Gate 5 MMIO mapping established for 0x%I64X bytes.\n",
        (ULONGLONG)context->MmioLength);

    status = BootCampSmcGate5DGetF0MxKeyInfo(context);
    if (!NT_SUCCESS(status))
    {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) transaction failed: 0x%08X; physical retry intentionally suppressed.\n",
            status);
        return STATUS_SUCCESS;
    }

    status = BootCampSmcGate5DGetF1MxKeyInfo(context);
    if (!NT_SUCCESS(status))
    {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) transaction failed: 0x%08X; physical retry intentionally suppressed.\n",
            status);
        return STATUS_SUCCESS;
    }

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx/F1Mx) transactions completed successfully.\n");

    return STATUS_SUCCESS;
}

static
NTSTATUS
BootCampSmcGate5DGetF0MxKeyInfo(
    _In_ PBOOTCAMP_SMC_DEVICE_CONTEXT context)
{
    volatile ULONG* responseTypeRegister;
    volatile UCHAR* responseLengthRegister;
    volatile UCHAR* responseAttributesRegister;
    volatile ULONG* keyNameRegister;
    volatile UCHAR* smcIdRegister;
    volatile UCHAR* commandResultRegister;
    volatile UCHAR* statusRegister;
    UCHAR initialStatus;
    UCHAR status;
    UCHAR commandResult;
    ULONG responseType;
    UCHAR responseLength;
    UCHAR responseAttributes;
    ULONG pollIndex;
    ULONG pollCount;
    LARGE_INTEGER pollInterval;
    BOOLEAN staleStatusCleared;

    PAGED_CODE();

    responseTypeRegister = (volatile ULONG*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_RESPONSE_TYPE_OFFSET);
    responseLengthRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_RESPONSE_LENGTH_OFFSET);
    responseAttributesRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_RESPONSE_ATTRIBUTES_OFFSET);
    keyNameRegister = (volatile ULONG*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_KEY_NAME_OFFSET);
    smcIdRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_SMC_ID_OFFSET);
    commandResultRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_COMMAND_RESULT_OFFSET);
    statusRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_STATUS_OFFSET);

    staleStatusCleared = FALSE;
    status = 0;

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) start.\n");

    initialStatus = READ_REGISTER_UCHAR(statusRegister);

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) initial status=0x%02X.\n",
        initialStatus);

    if (initialStatus != 0)
    {
        WRITE_REGISTER_UCHAR(statusRegister, 0);
        staleStatusCleared = TRUE;
    }

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) stale status cleared=%u.\n",
        staleStatusCleared ? 1u : 0u);

    WRITE_REGISTER_ULONG(keyNameRegister, BOOTCAMP_SMC_KEY_F0MX);
    WRITE_REGISTER_UCHAR(smcIdRegister, 0);
    WRITE_REGISTER_UCHAR(
        commandResultRegister,
        BOOTCAMP_SMC_COMMAND_GET_KEY_INFO);

    pollInterval.QuadPart = BOOTCAMP_SMC_GET_KEY_INFO_F0MX_POLL_INTERVAL_100NS;

    for (pollIndex = 0;
         pollIndex < BOOTCAMP_SMC_GET_KEY_INFO_F0MX_MAX_POLL_COUNT;
         ++pollIndex)
    {
        status = READ_REGISTER_UCHAR(statusRegister);

        if (status & BOOTCAMP_SMC_GET_KEY_INFO_F0MX_STATUS_COMPLETE_MASK)
        {
            break;
        }

        KeDelayExecutionThread(
            KernelMode,
            FALSE,
            &pollInterval);
    }

    pollCount = (pollIndex < BOOTCAMP_SMC_GET_KEY_INFO_F0MX_MAX_POLL_COUNT) ?
        (pollIndex + 1u) :
        pollIndex;

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) poll count=%lu final status=0x%02X completion NTSTATUS=0x%08X.\n",
        pollCount,
        status,
        ((status & BOOTCAMP_SMC_GET_KEY_INFO_F0MX_STATUS_COMPLETE_MASK) == 0) ?
            STATUS_IO_TIMEOUT :
            STATUS_SUCCESS);

    if ((status &
         BOOTCAMP_SMC_GET_KEY_INFO_F0MX_STATUS_COMPLETE_MASK) == 0)
    {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) timeout failure: 0x%08X.\n",
            STATUS_IO_TIMEOUT);
        return STATUS_IO_TIMEOUT;
    }

    commandResult = READ_REGISTER_UCHAR(commandResultRegister);

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) command result=0x%02X protocol NTSTATUS=0x%08X.\n",
        commandResult,
        (commandResult != BOOTCAMP_SMC_GET_KEY_INFO_F0MX_RESULT_SUCCESS) ?
            STATUS_DEVICE_PROTOCOL_ERROR :
            STATUS_SUCCESS);

    if (commandResult !=
        BOOTCAMP_SMC_GET_KEY_INFO_F0MX_RESULT_SUCCESS)
    {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) protocol failure: 0x%08X.\n",
            STATUS_DEVICE_PROTOCOL_ERROR);
        return STATUS_DEVICE_PROTOCOL_ERROR;
    }

    responseType = READ_REGISTER_ULONG(responseTypeRegister);
    responseLength = READ_REGISTER_UCHAR(responseLengthRegister);
    responseAttributes = READ_REGISTER_UCHAR(responseAttributesRegister);

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F0Mx) raw metadata type=0x%08lX length=0x%02X attributes=0x%02X.\n",
        responseType,
        responseLength,
        responseAttributes);

    return STATUS_SUCCESS;
}

static
NTSTATUS
BootCampSmcGate5DGetF1MxKeyInfo(
    _In_ PBOOTCAMP_SMC_DEVICE_CONTEXT context)
{
    volatile ULONG* responseTypeRegister;
    volatile UCHAR* responseLengthRegister;
    volatile UCHAR* responseAttributesRegister;
    volatile ULONG* keyNameRegister;
    volatile UCHAR* smcIdRegister;
    volatile UCHAR* commandResultRegister;
    volatile UCHAR* statusRegister;
    UCHAR initialStatus;
    UCHAR status;
    UCHAR commandResult;
    ULONG responseType;
    UCHAR responseLength;
    UCHAR responseAttributes;
    ULONG pollIndex;
    ULONG pollCount;
    LARGE_INTEGER pollInterval;
    BOOLEAN staleStatusCleared;

    PAGED_CODE();

    responseTypeRegister = (volatile ULONG*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_RESPONSE_TYPE_OFFSET);
    responseLengthRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_RESPONSE_LENGTH_OFFSET);
    responseAttributesRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_RESPONSE_ATTRIBUTES_OFFSET);
    keyNameRegister = (volatile ULONG*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_KEY_NAME_OFFSET);
    smcIdRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_SMC_ID_OFFSET);
    commandResultRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_COMMAND_RESULT_OFFSET);
    statusRegister = (volatile UCHAR*)(
        (PUCHAR)context->MmioBase + BOOTCAMP_SMC_MMIO_STATUS_OFFSET);

    staleStatusCleared = FALSE;
    status = 0;

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) start.\n");

    initialStatus = READ_REGISTER_UCHAR(statusRegister);

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) initial status=0x%02X.\n",
        initialStatus);

    if (initialStatus != 0)
    {
        WRITE_REGISTER_UCHAR(statusRegister, 0);
        staleStatusCleared = TRUE;
    }

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) stale status cleared=%u.\n",
        staleStatusCleared ? 1u : 0u);

    WRITE_REGISTER_ULONG(keyNameRegister, BOOTCAMP_SMC_KEY_F1MX);
    WRITE_REGISTER_UCHAR(smcIdRegister, 0);
    WRITE_REGISTER_UCHAR(
        commandResultRegister,
        BOOTCAMP_SMC_COMMAND_GET_KEY_INFO);

    pollInterval.QuadPart = BOOTCAMP_SMC_GET_KEY_INFO_F1MX_POLL_INTERVAL_100NS;

    for (pollIndex = 0;
         pollIndex < BOOTCAMP_SMC_GET_KEY_INFO_F1MX_MAX_POLL_COUNT;
         ++pollIndex)
    {
        status = READ_REGISTER_UCHAR(statusRegister);

        if (status & BOOTCAMP_SMC_GET_KEY_INFO_F1MX_STATUS_COMPLETE_MASK)
        {
            break;
        }

        KeDelayExecutionThread(
            KernelMode,
            FALSE,
            &pollInterval);
    }

    pollCount = (pollIndex < BOOTCAMP_SMC_GET_KEY_INFO_F1MX_MAX_POLL_COUNT) ?
        (pollIndex + 1u) :
        pollIndex;

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) poll count=%lu final status=0x%02X completion NTSTATUS=0x%08X.\n",
        pollCount,
        status,
        ((status & BOOTCAMP_SMC_GET_KEY_INFO_F1MX_STATUS_COMPLETE_MASK) == 0) ?
            STATUS_IO_TIMEOUT :
            STATUS_SUCCESS);

    if ((status &
         BOOTCAMP_SMC_GET_KEY_INFO_F1MX_STATUS_COMPLETE_MASK) == 0)
    {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) timeout failure: 0x%08X.\n",
            STATUS_IO_TIMEOUT);
        return STATUS_IO_TIMEOUT;
    }

    commandResult = READ_REGISTER_UCHAR(commandResultRegister);

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) command result=0x%02X protocol NTSTATUS=0x%08X.\n",
        commandResult,
        (commandResult != BOOTCAMP_SMC_GET_KEY_INFO_F1MX_RESULT_SUCCESS) ?
            STATUS_DEVICE_PROTOCOL_ERROR :
            STATUS_SUCCESS);

    if (commandResult !=
        BOOTCAMP_SMC_GET_KEY_INFO_F1MX_RESULT_SUCCESS)
    {
        DbgPrintEx(
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) protocol failure: 0x%08X.\n",
            STATUS_DEVICE_PROTOCOL_ERROR);
        return STATUS_DEVICE_PROTOCOL_ERROR;
    }

    responseType = READ_REGISTER_ULONG(responseTypeRegister);
    responseLength = READ_REGISTER_UCHAR(responseLengthRegister);
    responseAttributes = READ_REGISTER_UCHAR(responseAttributesRegister);

    DbgPrintEx(
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: Gate 5D-B GET_KEY_INFO(F1Mx) raw metadata type=0x%08lX length=0x%02X attributes=0x%02X.\n",
        responseType,
        responseLength,
        responseAttributes);

    return STATUS_SUCCESS;
}

NTSTATUS
BootCampSmcEvtDeviceReleaseHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesTranslated)
{
    PBOOTCAMP_SMC_DEVICE_CONTEXT context;

    UNREFERENCED_PARAMETER(ResourcesTranslated);
    PAGED_CODE();

    context = BootCampSmcGetDeviceContext(Device);

    if (context->MmioBase != NULL)
    {
        MmUnmapIoSpace(context->MmioBase, context->MmioLength);
    }

    RtlZeroMemory(context, sizeof(*context));

    KdPrintEx((
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: hardware resources released and MMIO mapping removed.\n"));

    return STATUS_SUCCESS;
}
