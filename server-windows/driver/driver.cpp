// driver.cpp - Indirect Display Driver (IDD) para OpenWirelessDisplay
// ============================================================================
// SCAFFOLD (Fase 1 de la guia). Provee un monitor virtual 1920x1080@60Hz via la
// API IddCx (UMDF 2.0). NO compila sin el Windows Driver Kit (WDK). Sirve como
// punto de partida fiel para el "modo monitor extendido".
//
// Build: Visual Studio + WDK -> proyecto "User Mode Driver (UMDF V2)".
// Firma: durante desarrollo habilitar test-signing (bcdedit /set testsigning on).
//
// Referencia oficial: https://github.com/microsoft/Windows-driver-samples
//                     -> video/IndirectDisplay
// ============================================================================

#include "Driver.h"

using namespace OpenWirelessDisplay;
using namespace Microsoft::WRL;

// ----------------------------------------------------------------------------
// EDID minimo para anunciar soporte 1920x1080. En produccion, generar un EDID
// valido con checksum correcto (aqui se referencia un blob a completar).
// ----------------------------------------------------------------------------
static const DWORD MonitorEdidLength = 128;
static const BYTE MonitorEdid[MonitorEdidLength] = {
    0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, // header fijo EDID
    0x36, 0x8C, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, // fabricante "OWD" + producto
    // ... (rellenar bloque de 128 bytes con timings 1920x1080 y checksum) ...
};

// Modo de pantalla soportado.
struct DisplayMode { DWORD Width; DWORD Height; DWORD VSync; };
static const DisplayMode s_SupportedModes[] = {
    { 1920, 1080, 60 },
    { 1600,  900, 60 },
    { 1280,  720, 60 },
};

// ----------------------------------------------------------------------------
// Punto de entrada del driver.
// ----------------------------------------------------------------------------
extern "C" NTSTATUS DriverEntry(PDRIVER_OBJECT pDriverObject, PUNICODE_STRING pRegistryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);

    WDF_DRIVER_CONFIG_INIT(&config, OwdDeviceAdd);
    config.DriverPoolTag = 'DWO ';

    return WdfDriverCreate(pDriverObject, pRegistryPath, &attributes, &config, WDF_NO_HANDLE);
}

// ----------------------------------------------------------------------------
// EvtDriverDeviceAdd: inicializa el adaptador virtual via IddCx.
// ----------------------------------------------------------------------------
NTSTATUS OwdDeviceAdd(WDFDRIVER /*Driver*/, PWDFDEVICE_INIT pDeviceInit)
{
    NTSTATUS status = STATUS_SUCCESS;

    // 1) Preparar el device para IddCx.
    IDD_CX_CLIENT_CONFIG iddConfig;
    IDD_CX_CLIENT_CONFIG_INIT(&iddConfig);
    iddConfig.EvtIddCxAdapterInitFinished      = OwdAdapterInitFinished;
    iddConfig.EvtIddCxAdapterCommitModes       = OwdAdapterCommitModes;
    iddConfig.EvtIddCxParseMonitorDescription  = OwdParseMonitorDescription;
    iddConfig.EvtIddCxMonitorGetDefaultDescriptionModes = OwdMonitorGetDefaultModes;
    iddConfig.EvtIddCxMonitorQueryTargetModes  = OwdMonitorQueryModes;
    iddConfig.EvtIddCxMonitorAssignSwapChain   = OwdMonitorAssignSwapChain;
    iddConfig.EvtIddCxMonitorUnassignSwapChain = OwdMonitorUnassignSwapChain;

    status = IddCxDeviceInitConfig(pDeviceInit, &iddConfig);
    if (!NT_SUCCESS(status)) return status;

    // 2) Crear el WDFDEVICE con su contexto.
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, IndirectDeviceContext);

    WDF_PNPPOWER_EVENT_CALLBACKS pnpPowerCallbacks;
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnpPowerCallbacks);
    pnpPowerCallbacks.EvtDeviceD0Entry = OwdDeviceD0Entry;
    WdfDeviceInitSetPnpPowerEventCallbacks(pDeviceInit, &pnpPowerCallbacks);

    WDFDEVICE device = nullptr;
    status = WdfDeviceCreate(&pDeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) return status;

    status = IddCxDeviceInitialize(device);
    auto* ctx = WdfObjectGet_IndirectDeviceContext(device);
    ctx->Device = device;
    return status;
}

NTSTATUS OwdDeviceD0Entry(WDFDEVICE Device, WDF_POWER_DEVICE_STATE /*PreviousState*/)
{
    auto* ctx = WdfObjectGet_IndirectDeviceContext(Device);

    // Dar de alta el adaptador virtual.
    IDDCX_ADAPTER_CAPS caps = {};
    caps.Size = sizeof(caps);
    caps.MaxMonitorsSupported = 1;
    caps.EndPointDiagnostics.Size = sizeof(caps.EndPointDiagnostics);
    caps.EndPointDiagnostics.GammaSupport = IDDCX_FEATURE_IMPLEMENTATION_NONE;
    caps.EndPointDiagnostics.TransmissionType = IDDCX_TRANSMISSION_TYPE_WIRED_OTHER;
    caps.EndPointDiagnostics.pEndPointFriendlyName = L"OpenWirelessDisplay Monitor";

    WDF_OBJECT_ATTRIBUTES adapterAttr;
    WDF_OBJECT_ATTRIBUTES_INIT(&adapterAttr);

    IDARG_IN_ADAPTER_INIT initIn = {};
    initIn.WdfDevice = Device;
    initIn.pCaps = &caps;
    initIn.ObjectAttributes = &adapterAttr;

    IDARG_OUT_ADAPTER_INIT initOut = {};
    NTSTATUS status = IddCxAdapterInitAsync(&initIn, &initOut);
    if (NT_SUCCESS(status)) ctx->Adapter = initOut.AdapterObject;
    return status;
}

// ----------------------------------------------------------------------------
// El adaptador termino de inicializar: crear el monitor virtual con su EDID.
// ----------------------------------------------------------------------------
NTSTATUS OwdAdapterInitFinished(IDDCX_ADAPTER AdapterObject, const IDARG_IN_ADAPTER_INIT_FINISHED* pInArgs)
{
    if (!NT_SUCCESS(pInArgs->AdapterInitStatus)) return STATUS_SUCCESS;

    IDDCX_MONITOR_INFO monitorInfo = {};
    monitorInfo.Size = sizeof(monitorInfo);
    monitorInfo.MonitorType = IDDCX_MONITOR_TYPE_HDMI_INTERFACE;
    monitorInfo.ConnectorIndex = 0;
    monitorInfo.MonitorDescription.Size = sizeof(monitorInfo.MonitorDescription);
    monitorInfo.MonitorDescription.Type = IDDCX_MONITOR_DESCRIPTION_TYPE_EDID;
    monitorInfo.MonitorDescription.DataSize = MonitorEdidLength;
    monitorInfo.MonitorDescription.pData = const_cast<BYTE*>(MonitorEdid);

    WDF_OBJECT_ATTRIBUTES monitorAttr;
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&monitorAttr, IndirectMonitorContext);

    IDARG_IN_MONITORCREATE createIn = {};
    createIn.ObjectAttributes = &monitorAttr;
    createIn.pMonitorInfo = &monitorInfo;

    IDARG_OUT_MONITORCREATE createOut = {};
    NTSTATUS status = IddCxMonitorCreate(AdapterObject, &createIn, &createOut);
    if (!NT_SUCCESS(status)) return status;

    // Anunciar el monitor como conectado (llegada).
    IDARG_IN_MONITORARRIVAL arrivalIn = { createOut.MonitorObject };
    IDARG_OUT_MONITORARRIVAL arrivalOut = {};
    return IddCxMonitorArrival(createOut.MonitorObject, &arrivalOut);
}

// ----------------------------------------------------------------------------
// Resto de callbacks: declarados como TODO del scaffold. Implementar segun el
// sample oficial de IndirectDisplay (modos, swapchain, render thread).
// ----------------------------------------------------------------------------
NTSTATUS OwdAdapterCommitModes(IDDCX_ADAPTER, const IDARG_IN_COMMITMODES*) { return STATUS_SUCCESS; }

NTSTATUS OwdParseMonitorDescription(const IDARG_IN_PARSEMONITORDESCRIPTION*, IDARG_OUT_PARSEMONITORDESCRIPTION*)
{
    // TODO: parsear el EDID y exponer los modos en s_SupportedModes.
    return STATUS_SUCCESS;
}

NTSTATUS OwdMonitorGetDefaultModes(IDDCX_MONITOR, const IDARG_IN_GETDEFAULTDESCRIPTIONMODES*, IDARG_OUT_GETDEFAULTDESCRIPTIONMODES*)
{
    // TODO: devolver s_SupportedModes como modos por defecto.
    return STATUS_SUCCESS;
}

NTSTATUS OwdMonitorQueryModes(IDDCX_MONITOR, const IDARG_IN_QUERYTARGETMODES*, IDARG_OUT_QUERYTARGETMODES*)
{
    // TODO: reportar los target modes soportados (1920x1080@60, etc.).
    return STATUS_SUCCESS;
}

NTSTATUS OwdMonitorAssignSwapChain(IDDCX_MONITOR, const IDARG_IN_SETSWAPCHAIN*)
{
    // TODO: arrancar el hilo de procesamiento que toma frames del swapchain
    // (ID3D11Texture2D) y los entrega a la app de captura/codificacion en modo usuario.
    return STATUS_SUCCESS;
}

NTSTATUS OwdMonitorUnassignSwapChain(IDDCX_MONITOR)
{
    // TODO: detener el hilo de procesamiento del swapchain.
    return STATUS_SUCCESS;
}
