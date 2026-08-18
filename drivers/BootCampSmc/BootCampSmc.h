#pragma once

#include <ntddk.h>
#include <wdf.h>

typedef struct _BOOTCAMP_SMC_DEVICE_CONTEXT
{
    ULONG RawResourceCount;
    ULONG TranslatedResourceCount;

    BOOLEAN HasPortResource;
    BOOLEAN HasMemoryResource;
    BOOLEAN HasInterruptResource;
    UCHAR Reserved;

    PHYSICAL_ADDRESS PortStart;
    ULONG PortLength;

    PHYSICAL_ADDRESS MemoryStart;
    ULONG MemoryLength;

    ULONG InterruptLevel;
    ULONG InterruptVector;
    KAFFINITY InterruptAffinity;
} BOOTCAMP_SMC_DEVICE_CONTEXT, *PBOOTCAMP_SMC_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(
    BOOTCAMP_SMC_DEVICE_CONTEXT,
    BootCampSmcGetDeviceContext);

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD BootCampSmcEvtDeviceAdd;
EVT_WDF_DEVICE_PREPARE_HARDWARE BootCampSmcEvtDevicePrepareHardware;
EVT_WDF_DEVICE_RELEASE_HARDWARE BootCampSmcEvtDeviceReleaseHardware;
