using System;
using UnityEngine;

#if ENABLE_WINMD_SUPPORT
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Foundation;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Security.Cryptography;

#endif

public class BLEManager : MonoBehaviour
{
    public static BLEManager Instance { get; private set; }

    private readonly string dataUuid = "09bf2c52-d1d9-c0b7-4145-475964544307";
    private readonly string commandUuid = "d5913036-2d8a-41ee-85b9-4e361aa5c8a7";
    private readonly string serviceUuid = "c8c0a708-e361-4b5e-a365-98fa6b0a836f";

    public event Action OnDeviceConnected;

    private string _deviceAddress;
    private string _serviceUUID;

#if ENABLE_WINMD_SUPPORT
    private BluetoothLEDevice _bleDevice;
    private GattCharacteristic _dataCharacteristic;
    private GattCharacteristic _commandCharacteristic;
    private BluetoothLEAdvertisementWatcher _watcher;
#endif

    public BLEStatus status = BLEStatus.Disconnected;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Expose StartScan and SendByte outside of the conditional compilation.
    public void StartScan()
    {
        Debug.Log("Starting BLE scan for UWP!");
#if ENABLE_WINMD_SUPPORT
        StartScanUWP();
#endif
    }

    public void SendByte()
    {
#if ENABLE_WINMD_SUPPORT
        if (_commandCharacteristic != null)
        {
            byte[] buffer = new byte[7];
            buffer[0] = 0x02;
            buffer[1] = 0x05;
            buffer[2] = 0x08;
            buffer[3] = 0x10; // Sample value
            buffer[4] = 0x00;
            buffer[5] = 0x00;
            buffer[6] = 0x01;

            WriteToCommandCharacteristic(buffer);
        }
#endif
    }

#if ENABLE_WINMD_SUPPORT
    private void StartScanUWP()
    {
        Debug.Log("Scanning for BLE devices...");
        _watcher = new BluetoothLEAdvertisementWatcher();

        _watcher.Received += OnAdvertisementReceived;
        _watcher.Start();
    }

    private void StopScanning()
    {
        if (_watcher != null)
        {
            _watcher.Stop();
            _watcher.Received -= OnAdvertisementReceived;
            Debug.Log("Stopped scanning for BLE devices.");
        }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        Debug.Log($"Found device: {args.Advertisement.LocalName}");

        // Check if the device name matches the one you're looking for (e.g., "muse_v3")
        if (args.Advertisement.LocalName.Contains("muse_v3"))
        {
            _deviceAddress = args.BluetoothAddress.ToString();
            StopScanning();  // Stop scanning after finding the device
            ConnectToDevice(args);
        }
    }

    private async void ConnectToDevice(BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
Debug.Log("eccoci");
            _bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
Debug.Log("eccoci qui");
            if (_bleDevice == null)
            {
                Debug.LogError("Failed to connect to device.");
                return;
            }

            var services = await _bleDevice.GetGattServicesAsync();

            foreach (var service in services.Services)
            {
                Debug.Log($"Found service: {service.Uuid}");

                if (service.Uuid == new Guid(serviceUuid))
                {
                    await ConnectToServiceAsync(service);
                }
            }

            status = BLEStatus.Connected;
            OnDeviceConnected?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error connecting to device: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task ConnectToServiceAsync(GattDeviceService service)
    {
        var characteristics = await service.GetCharacteristicsAsync();

        foreach (var characteristic in characteristics.Characteristics)
        {
            Debug.Log($"Found characteristic: {characteristic.Uuid}");

            if (characteristic.Uuid == new Guid(commandUuid))
            {
                _commandCharacteristic = characteristic;
                await SubscribeToCommandCharacteristicAsync();
            }
            else if (characteristic.Uuid == new Guid(dataUuid))
            {
                _dataCharacteristic = characteristic;
                await SubscribeToDataCharacteristicAsync();
            }
        }
    }

    private async System.Threading.Tasks.Task SubscribeToCommandCharacteristicAsync()
    {
        if (_commandCharacteristic == null) return;

        var status = await _commandCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

        if (status == GattCommunicationStatus.Success)
        {
            _commandCharacteristic.ValueChanged += CommandCharacteristic_ValueChanged;
            Debug.Log("Subscribed to command characteristic.");
        }
        else
        {
            Debug.LogError("Failed to subscribe to command characteristic.");
        }
    }

    private void CommandCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
Debug.Log("something changed");
        var value = ConvertIBufferToByteArray(args.CharacteristicValue);
        Debug.Log($"Command characteristic value changed: {BitConverter.ToString(value)}");
    }

    private async System.Threading.Tasks.Task SubscribeToDataCharacteristicAsync()
{
    if (_dataCharacteristic == null) 
    {
        Debug.LogError("Data characteristic is null.");
        return;
    }

    // Check for Notify or Indicate properties
    var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
    if (_dataCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
    {
        cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
    }
    else if (_dataCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
    {
        cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
    }

    if (cccdValue == GattClientCharacteristicConfigurationDescriptorValue.None)
    {
        Debug.LogError("Cannot subscribe: Characteristic does not support Notify or Indicate.");
        return;
    }

    // Attempt subscription
    var status = await _dataCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

    if (status == GattCommunicationStatus.Success)
    {
        Debug.Log("Successfully subscribed to data characteristic.");
        _dataCharacteristic.ValueChanged += DataCharacteristic_ValueChanged;
    }
    else
    {
        Debug.LogError($"Failed to subscribe to data characteristic. Status: {status}");
    }
}


    private void DataCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
Debug.Log("data changed");
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] value);

    // Log the raw byte array as a hexadecimal string
    Debug.Log($"Data characteristic value changed (hex): {BitConverter.ToString(value)}");

    }

    private byte[] ConvertIBufferToByteArray(IBuffer buffer)
    {
        byte[] byteArray = new byte[buffer.Length];
        var dataReader = DataReader.FromBuffer(buffer);
        dataReader.LoadAsync((uint)buffer.Length).AsTask().Wait();  // Ensure buffer is loaded
        dataReader.ReadBytes(byteArray);  // Extract the byte data
        return byteArray;
    }

    private async void WriteToCommandCharacteristic(byte[] buffer)
    {
Debug.Log("in write");
        if (_commandCharacteristic == null)
            return;
Debug.Log("in write 2");
        var writer = new DataWriter();
        writer.WriteBytes(buffer);

        try
        {
            var result = await _commandCharacteristic.WriteValueAsync(writer.DetachBuffer());
            if (result == GattCommunicationStatus.Success)
            {
                Debug.Log("Write succeeded to command characteristic.");
            }
            else
            {
                Debug.LogError("Failed to write to command characteristic.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error writing to command characteristic: {ex.Message}");
        }
    }

#endif

    public enum BLEStatus
    {
        Disconnected,
        Connected,
        Streaming
    }
}
