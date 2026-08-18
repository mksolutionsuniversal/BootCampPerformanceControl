#include "BootCampSmc.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, BootCampSmcEvtDeviceAdd)
#pragma alloc_text(PAGE, BootCampSmcEvtDevicePrepareHardware)
#pragma alloc_text(PAGE, BootCampSmcEvtDeviceReleaseHardware)
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

    context->MmioLength = (SIZE_T)context->MemoryLength;
    context->MmioBase = MmMapIoSpaceEx(
        context->MemoryStart,
        context->MmioLength,
        PAGE_READONLY | PAGE_NOCACHE);

    if (context->MmioBase == NULL)
    {
        context->MmioLength = 0;

        KdPrintEx((
            DPFLTR_IHVDRIVER_ID,
            DPFLTR_ERROR_LEVEL,
            "BootCampSmc: read-only MMIO mapping failed.\n"));
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    KdPrintEx((
        DPFLTR_IHVDRIVER_ID,
        DPFLTR_INFO_LEVEL,
        "BootCampSmc: read-only non-cached MMIO mapping established for 0x%I64X bytes; no register was read or written.\n",
        (ULONGLONG)context->MmioLength));

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
        "BootCampSmc: hardware resources released and MMIO mapping removed; no register was read or written.\n"));

    return STATUS_SUCCESS;
}
