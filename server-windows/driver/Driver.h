// Driver.h - Indirect Display Driver (IDD) para OpenWirelessDisplay
// Fase 1 de la guia. Requiere Windows Driver Kit (WDK) + Visual Studio.
#pragma once

#include <windows.h>
#include <bugcodes.h>
#include <wudfwdm.h>
#include <wdf.h>
#include <iddcx.h>

#include <dxgi1_5.h>
#include <d3d11_2.h>
#include <avrt.h>
#include <wrl.h>

#include <memory>
#include <vector>

namespace OpenWirelessDisplay
{
    // Contexto del adaptador virtual.
    struct IndirectDeviceContext
    {
        WDFDEVICE Device = nullptr;
        IDDCX_ADAPTER Adapter = nullptr;
    };

    struct IndirectMonitorContext
    {
        IDDCX_MONITOR Monitor = nullptr;
    };
}

// Macros de contexto WDF.
WDF_DECLARE_CONTEXT_TYPE(OpenWirelessDisplay::IndirectDeviceContext);
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(OpenWirelessDisplay::IndirectMonitorContext, GetMonitorContext);

extern "C" DRIVER_INITIALIZE DriverEntry;

EVT_WDF_DRIVER_DEVICE_ADD OwdDeviceAdd;
EVT_WDF_DEVICE_D0_ENTRY OwdDeviceD0Entry;

EVT_IDD_CX_ADAPTER_INIT_FINISHED OwdAdapterInitFinished;
EVT_IDD_CX_ADAPTER_COMMIT_MODES OwdAdapterCommitModes;
EVT_IDD_CX_PARSE_MONITOR_DESCRIPTION OwdParseMonitorDescription;
EVT_IDD_CX_MONITOR_GET_DEFAULT_DESCRIPTION_MODES OwdMonitorGetDefaultModes;
EVT_IDD_CX_MONITOR_QUERY_TARGET_MODES OwdMonitorQueryModes;
EVT_IDD_CX_MONITOR_ASSIGN_SWAPCHAIN OwdMonitorAssignSwapChain;
EVT_IDD_CX_MONITOR_UNASSIGN_SWAPCHAIN OwdMonitorUnassignSwapChain;
