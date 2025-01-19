using System;
using System.IO.Ports;

public static class SerialPortHelper
{
    /// <summary>
    /// Sends "AT;" to the specified serial port (9600 8N1) and reads back a response.
    /// </summary>
    /// <param name="portName">The name of the serial port (e.g., "COM3").</param>
    /// <returns>The response from the device or an error message if it timed out.</returns>
    public static string SendATCommand(string portName, string command)
    {
        // Set up serial port with desired settings:
        using (SerialPort serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One))
        {
            // Optionally configure timeouts
            serialPort.ReadTimeout = 3000;   // ms
            serialPort.WriteTimeout = 3000;  // ms

            // Open the port
            serialPort.Open();

            // Clear buffers just in case
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            // Write the command
            serialPort.WriteLine(command);

            try
            {
                // ReadLine will wait until it gets a line terminator (\n by default)
                string response = serialPort.ReadLine();
                return response;
            }
            catch (TimeoutException)
            {
                // If no response is received within ReadTimeout, we catch a TimeoutException
                return "No response or timed out.";
            }
            catch (Exception ex)
            {
                // Catch other possible exceptions (e.g., I/O exceptions)
                return $"Error: {ex.Message}";
            }
        }
    }
}
