using RegistryMonitors;
using System.IO.Ports;

namespace OnAirMonitor
{

    internal class Program
    {
        private static bool isOnAirTurnedOn = false;

        /*
         *
         *Default UART Setting: Baud rate：9600 ;Stop Bits:1 ;Parity：None; Data Bits：8
            1, AT ; Test Command, you will get "OK"
            2, AT+CH1=1; Close the relay of channel 1
            3, AT+CH1=0; Turn on the relay of channel 1
            4, AT+BAUD=115200; Modify the baud rate to 115200
         */
        static async Task Main(string[] args)
        {
            var ports = GetAllComPorts();

            if (ports.Count() == 0)
            {
                Console.WriteLine("No serial ports detected, is device plugged in");
            }
            else
            {
                if (ports.Count() > 1)
                {
                    Console.WriteLine($"You have more than one serial device.  In 2025 why would you have more than 1 serial device -- found {string.Join(", ", ports)}");
                }
                else
                {
                    var serialPort = ports[0];
                    Console.WriteLine($"Found serial port : {serialPort}");
                    if (!Test(serialPort))
                    {
                        Console.WriteLine("Device not responding.");
                        return;
                    }

                    TurnOff(serialPort);

                    await CheckViaRegistryNotificationAsync(serialPort);

                    //await CheckViaPollingAsync();
                }
            }

        }

        private static async Task CheckViaRegistryNotificationAsync(string port)
        {
            // The parent path for "NonPackaged"
            const string PARENT_PATH =
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged";

            using var multiMonitor = new MultiSubKeyMonitor(PARENT_PATH);

            // Subscribe to event
            multiMonitor.SubKeyValueChanged += (sender, info) =>
            {
                if (!info.newValue.HasValue)
                {
                    Console.WriteLine($"SubKey '{info.subKeyName}' changed LastUsedTimeStamp -- error reading value");
                    TurnOn(port); //turn on led to bring attention
                }
                else
                {
                    if (info.newValue == 0)
                    {
                        Console.WriteLine($"{DateTime.Now.ToString()} - Camera Turned on");
                        TurnOn(port);
                    }
                    else
                    {
                        var cameraTurnedOffOn = DateTime.FromFileTimeUtc(info.newValue.Value);

                        Console.WriteLine($"{DateTime.Now.ToString()} - Camera Turned off @ {cameraTurnedOffOn.ToLocalTime().ToString()}");

                        TurnOff(port);
                    }
                }
            };

            // Initialize & start monitoring
            multiMonitor.InitializeAndStart();

            Console.WriteLine("Monitoring all subkeys under NonPackaged. Press 'q' to quit.");
            // Keep reading keys until 'q' is pressed
            while (true)
            {
                // ReadKey(true) reads a key press without echoing to the console
                var keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.Q)
                    break;
            }


            // Stop
            multiMonitor.StopAll();
        }

        private static async Task CheckViaPollingAsync(string port)
        {
            while (true)
            {
                bool cameraInUse = CameraUsageChecker.IsCameraInUse();
                Console.WriteLine($"Is the camera in use? {cameraInUse}");
                if (cameraInUse != isOnAirTurnedOn)
                {
                    if (cameraInUse)
                    {
                        TurnOn(port);
                    }
                    else
                    {
                        TurnOff(port);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }
        private static bool Test(string port)
        {
            var response = SerialPortHelper.SendATCommand(port, "AT;");
            return (response == "OK");
        }

        private static void TurnOff(string port)
        {
            var response = SerialPortHelper.SendATCommand(port, "AT+CH1=0;");
            isOnAirTurnedOn = false;
            Console.WriteLine(response);
        }

        private static void TurnOn(string port)
        {
            var response = SerialPortHelper.SendATCommand(port, "AT+CH1=1;");
            isOnAirTurnedOn = true;
            Console.WriteLine(response);
        }


        /// <summary>
        /// Gets all available COM ports on the computer.
        /// </summary>
        /// <returns>An array of COM port names (e.g., COM1, COM2, etc.).</returns>
        public static string[] GetAllComPorts()
        {
            // Retrieve all available serial port names
            return SerialPort.GetPortNames();
        }

    }
}
