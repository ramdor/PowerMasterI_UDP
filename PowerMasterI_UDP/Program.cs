//MW0LGE 23/07/2024
//Provides a method to retreive foward/reverse/swr from an Array Solutions PowerMaster I
//and sends that data to a UDP endpoint in the format:
//  fwd:0.00:rev:0.00:swr:1.0
//command line args shown if none provided

/*
lite output captured
02 44 30 03 37 31 0D
02 42 3F 03 43 46 0D
02 62 3F 03 33 36 0D
02 55 3F 03 45 38 0D
02 57 3F 03 32 34 0D
02 77 3F 03 44 44 0D
02 43 3F 03 41 39 0D
02 64 3F 03 44 33 0D
02 48 3F 03 35 31 0D
02 68 3F 03 41 38 0D
02 49 3F 03 33 37 0D
02 6C 3F 03 38 31 0D
02 4C 3F 03 37 38 0D
02 4D 3F 03 31 45 0D
02 50 3F 03 41 37 0D
02 52 3F 03 36 42 0D
02 53 3F 03 30 44 0D
02 54 3F 03 38 45 0D
02 74 3F 03 37 37 0D
02 56 3F 03 34 32 0D
02 79 3F 03 36 41 0D
02 61 3F 03 39 43 0D
02 44 31 03 43 30 0D

buttons
fast
02 4D 34 03 35 32 0D 
02 4D 3F 03 31 45 0D

medium
02 4D 33 03 32 37 0D
02 4D 3F 03 31 45 0D

slow
02 4D 32 03 39 36 0D
02 4D 3F 03 31 45 0D

long
02 4D 31 03 46 34 0D
02 4D 3F 03 31 45 0D

vswr
02 4D 30 03 34 35 0D
02 4D 3F 03 31 45 0D
*/

using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

namespace SerialArraySolutions_PowerMasterI
{
    class Program
    {
        private static SerialPort serialPort;
        private static UdpClient udpClient;
        private static string endpointIP;
        private static int endpointPORT;

        static void Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.WriteLine("Usage: PowerMasterI_UDP.exe <endpointIP> <endpointPORT> <com> <baud>");
                return;
            }

            endpointIP = args[0];
            if (!int.TryParse(args[1], out endpointPORT))
            {
                Console.WriteLine("Invalid endpointPORT");
                return;
            }
            string com = args[2];
            int baud;
            if (!int.TryParse(args[3], out baud))
            {
                Console.WriteLine("Invalid baud rate");
                return;
            }

            Console.WriteLine($"Endpoint IP: {endpointIP}");
            Console.WriteLine($"Endpoint PORT: {endpointPORT}");
            Console.WriteLine($"COM Port: {com}");
            Console.WriteLine($"Baud Rate: {baud}");

            try
            {
                udpClient = new UdpClient();
                serialPort = new SerialPort(com, baud);
                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();
                SendData();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error opening serial port: " + ex.Message);
                return;
            }

            Console.WriteLine("Press Enter to exit...");
            Console.ReadLine();

            try
            {
                if (serialPort.IsOpen)
                {
                    // stop sending data
                    byte[] data = { 0x02, 0x44, 0x30, 0x03, 0x37, 0x31, 0x0D };
                    serialPort.Write(data, 0, data.Length);
                    serialPort.Write(data, 0, data.Length);

                    serialPort.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error closing serial port: " + ex.Message);
            }
        }

        private static void SendData()
        {
            try
            {
                if (serialPort.IsOpen)
                {
                    byte[] data = { 0x02, 0x44, 0x31, 0x03, 0x43, 0x30, 0x0D };
                    serialPort.Write(data, 0, data.Length);
                    serialPort.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending data: " + ex.Message);
            }
        }

        private static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (!serialPort.IsOpen)
                {
                    Console.WriteLine("Serial port closed, skipping data processing.");
                    return;
                }

                int n = serialPort.BytesToRead;
                byte[] data = new byte[n];
                int last_read = 0;
                for (int i = 0; i < n; i++)
                {
                    int b = serialPort.ReadByte();
                    if (b == -1) break;
                    data[i] = (byte)b;
                    last_read = i;
                }

                int startIndex = -1;
                int endIndex = -1;
                for (int i = 0; i < last_read; i++)
                {
                    if (data[i] == 2)
                    {
                        startIndex = i + 1;
                    }
                    else if (data[i] == 3)
                    {
                        endIndex = i - 1;
                        break;
                    }
                }

                if (startIndex > -1 && endIndex > -1)
                {
                    string result = Encoding.UTF8.GetString(data, startIndex, endIndex - startIndex);
                    string[] parts = result.Split(',');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        parts[i] = parts[i].Trim();
                    }

                    if (parts.Length >= 4 && parts[0] == "D")
                    {
                        float forward = 0, reflected = 0, swr = 0;
                        bool ok = float.TryParse(parts[1], out forward);
                        if (ok) ok = float.TryParse(parts[2], out reflected);
                        if (ok) ok = float.TryParse(parts[3], out swr);
                        if (ok)
                        {
                            if (swr < 1) swr = 1;
                            if (forward < 0) forward = 0;
                            if (reflected < 0) reflected = 0;
                            Byte[] sendBytes = Encoding.ASCII.GetBytes("fwd:" + forward.ToString("f2") + ":rev:" + reflected.ToString("f2") + ":swr:" + swr.ToString("f1"));
                            udpClient.Send(sendBytes, sendBytes.Length, endpointIP, endpointPORT);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error processing data received: " + ex.Message);
            }
        }
    }
}
