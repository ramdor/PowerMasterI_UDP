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
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace SerialArraySolutions_PowerMasterI
{
    class Program
    {
        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        private const byte TERM = 0x0d;

        private static SerialPort _serialPort;
        private static UdpClient _udpClient;
        private static string _endpointIP;
        private static int _endpointPORT;

        private static List<byte> _buffer;

        static void Main(string[] args)
        {
            //uncomment for testing
            //args = new string[4];
            //args[0] = "127.0.0.1";
            //args[1] = "10000";
            //args[2] = "com3";
            //args[3] = "38400";

            if (args.Length != 4)
            {
                Console.WriteLine("Usage: PowerMasterI_UDP.exe <endpointIP> <endpointPORT> <com> <baud>");
                return;
            }

            _endpointIP = args[0];
            if (!int.TryParse(args[1], out _endpointPORT))
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

            Console.WriteLine($"Endpoint IP: {_endpointIP}");
            Console.WriteLine($"Endpoint PORT: {_endpointPORT}");
            Console.WriteLine($"COM Port: {com}");
            Console.WriteLine($"Baud Rate: {baud}");

            _buffer = new List<byte>();

            try
            {
                _udpClient = new UdpClient();
                _serialPort = new SerialPort(com, baud, Parity.None, 8, StopBits.One);
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();
                SendData();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error opening serial port: " + ex.Message);
                return;
            }

            Console.WriteLine("Press Enter to exit...");
            Console.ReadLine();

            StopSendData();

            _buffer.Clear();
        }

        private static void SendData()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    //start sending data - D1 message
                    byte[] data = GenerateCRCPayload(Encoding.UTF8.GetBytes("D1"));
                    _serialPort.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending data: " + ex.Message);
            }
        }
        private static void StopSendData()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    //stop sending data - D0 message
                    byte[] data = GenerateCRCPayload(Encoding.UTF8.GetBytes("D0"));
                    _serialPort.Write(data, 0, data.Length);
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
                if (!_serialPort.IsOpen)
                {
                    Console.WriteLine("Serial port closed, skipping data processing.");
                    return;
                }

                int n = _serialPort.BytesToRead;
                for (int i = 0; i < n; i++)
                {
                    int b = _serialPort.ReadByte();
                    if (b == -1) break;
                    _buffer.Add((byte)b);
                }

                parseBuffer();

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error processing data received: " + ex.Message);
            }
        }
        private static void parseBuffer()
        {
            bool parsing = true;
            bool dump = false;
            while (parsing && _buffer.Count > 0)
            {
                if (_buffer[0] == STX)
                {
                    bool termFound = false;
                    // wait for TERM
                    for(int i = 1; i < _buffer.Count; i++)
                    {
                        if(_buffer[i] == TERM)
                        {
                            //we have whole msg block
                            int size = i + 1; // from stx to term inclusive

                            List<byte> byteRange = _buffer.GetRange(0, size);
                            byte[] msg = byteRange.ToArray();
                            _buffer.RemoveRange(0, size);

                            handleMessage(msg, 0, i - 3);

                            termFound = true;
                            break;
                        }
                        else if(i > 1024)
                        {
                            // some issue, no TERMS have been found in a resonable time, dump the entire buffer
                            dump = true;
                            break;
                        }
                    }

                    if (!termFound) parsing = false;
                }
                else
                {
                    // no stx at start
                    _buffer.RemoveAt(0);
                }
            }

            if (dump)
            {
                _buffer.Clear();
            }
        }
        private static void handleMessage(byte[] msg, int stx_pos, int etx_pos)
        {
            byte crc = CRCCheck(msg); //0 = ok crc, 1 = crc missmatch, 2 = missing etx

            //switch (crc)
            //{
            //    case 0:
            //        Console.WriteLine("CRC check passed.");
            //        break;
            //    case 1:
            //        Console.WriteLine("CRC check failed.");
            //        break;
            //    case 2:
            //        Console.WriteLine("Invalid data format or missing ETX.");
            //        break;
            //    default:
            //        Console.WriteLine("Unknown error.");
            //        break;
            //}

            if (crc == 0)
            {
                int size = etx_pos - stx_pos - 1;
                byte[] payload = new byte[size];
                Array.Copy(msg, stx_pos + 1, payload, 0, size);

                //Debug.Print(Encoding.UTF8.GetString(payload));

                string result = Encoding.UTF8.GetString(payload);
                string[] parts = result.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    parts[i] = parts[i].Trim();
                }

                if (parts.Length >= 4 && parts[0] == "D") // message type D - real time report
                {
                    float forward = 0, reflected = 0, vswr = 0;
                    bool ok = float.TryParse(parts[1], out forward);
                    if (ok) ok = float.TryParse(parts[2], out reflected);
                    if (ok) ok = float.TryParse(parts[3], out vswr);
                    if (ok)
                    {
                        bool vswr_read = vswr >= 1;
                        if (vswr < 1) vswr = 1;
                        if (forward < 0) forward = 0;
                        if (reflected < 0) reflected = 0;

                        string sendData = "vswr_read:" + vswr_read.ToString().ToLower() +
                                            ":forward:" + forward.ToString("f1") +
                                            ":reflected:" + reflected.ToString("f1") +
                                            ":vswr:" + vswr.ToString("f2");

                        if (parts.Length >= 5)
                        {
                            string[] status = parts[4].Split(';');
                            if (status.Length == 5)
                            {
                                sendData += ":vswr_alarm:" + (status[0] == "0" ? "false" : "true");
                                sendData += ":low_power_alarm:" + (status[1] == "0" ? "false" : "true");
                                sendData += ":high_power_alarm:" + (status[2] == "0" ? "false" : "true");
                                sendData += ":red_led:" + (status[3] == "0" ? "false" : "true");
                                sendData += ":yellow_led:" + (status[4] == "0" ? "false" : "true");
                            }
                        }

                        try
                        {
                            Byte[] sendBytes = Encoding.ASCII.GetBytes(sendData);
                            _udpClient.Send(sendBytes, sendBytes.Length, _endpointIP, _endpointPORT);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error sending udp data: " + ex.Message);
                            StopSendData();
                            _buffer.Clear();
                        }
                    }
                }
            }
        }

        // CRC based on provided code in documentation
        private static readonly byte[] crc8revtab = new byte[]
        {
        0x00, 0xB1, 0xD3, 0x62, 0x17, 0xA6, 0xC4, 0x75,
        0x2E, 0x9F, 0xFD, 0x4C, 0x39, 0x88, 0xEA, 0x5B,
        0x5C, 0xED, 0x8F, 0x3E, 0x4B, 0xFA, 0x98, 0x29,
        0x72, 0xC3, 0xA1, 0x10, 0x65, 0xD4, 0xB6, 0x07,
        0xB8, 0x09, 0x6B, 0xDA, 0xAF, 0x1E, 0x7C, 0xCD,
        0x96, 0x27, 0x45, 0xF4, 0x81, 0x30, 0x52, 0xE3,
        0xE4, 0x55, 0x37, 0x86, 0xF3, 0x42, 0x20, 0x91,
        0xCA, 0x7B, 0x19, 0xA8, 0xDD, 0x6C, 0x0E, 0xBF,
        0xC1, 0x70, 0x12, 0xA3, 0xD6, 0x67, 0x05, 0xB4,
        0xEF, 0x5E, 0x3C, 0x8D, 0xF8, 0x49, 0x2B, 0x9A,
        0x9D, 0x2C, 0x4E, 0xFF, 0x8A, 0x3B, 0x59, 0xE8,
        0xB3, 0x02, 0x60, 0xD1, 0xA4, 0x15, 0x77, 0xC6,
        0x79, 0xC8, 0xAA, 0x1B, 0x6E, 0xDF, 0xBD, 0x0C,
        0x57, 0xE6, 0x84, 0x35, 0x40, 0xF1, 0x93, 0x22,
        0x25, 0x94, 0xF6, 0x47, 0x32, 0x83, 0xE1, 0x50,
        0x0B, 0xBA, 0xD8, 0x69, 0x1C, 0xAD, 0xCF, 0x7E,
        0x33, 0x82, 0xE0, 0x51, 0x24, 0x95, 0xF7, 0x46,
        0x1D, 0xAC, 0xCE, 0x7F, 0x0A, 0xBB, 0xD9, 0x68,
        0x6F, 0xDE, 0xBC, 0x0D, 0x78, 0xC9, 0xAB, 0x1A,
        0x41, 0xF0, 0x92, 0x23, 0x56, 0xE7, 0x85, 0x34,
        0x8B, 0x3A, 0x58, 0xE9, 0x9C, 0x2D, 0x4F, 0xFE,
        0xA5, 0x14, 0x76, 0xC7, 0xB2, 0x03, 0x61, 0xD0,
        0xD7, 0x66, 0x04, 0xB5, 0xC0, 0x71, 0x13, 0xA2,
        0xF9, 0x48, 0x2A, 0x9B, 0xEE, 0x5F, 0x3D, 0x8C,
        0xF2, 0x43, 0x21, 0x90, 0xE5, 0x54, 0x36, 0x87,
        0xDC, 0x6D, 0x0F, 0xBE, 0xCB, 0x7A, 0x18, 0xA9,
        0xAE, 0x1F, 0x7D, 0xCC, 0xB9, 0x08, 0x6A, 0xDB,
        0x80, 0x31, 0x53, 0xE2, 0x97, 0x26, 0x44, 0xF5,
        0x4A, 0xFB, 0x99, 0x28, 0x5D, 0xEC, 0x8E, 0x3F,
        0x64, 0xD5, 0xB7, 0x06, 0x73, 0xC2, 0xA0, 0x11,
        0x16, 0xA7, 0xC5, 0x74, 0x01, 0xB0, 0xD2, 0x63,
        0x38, 0x89, 0xEB, 0x5A, 0x2F, 0x9E, 0xFC, 0x4D
        };

        private static byte b_CRC8;

        public static byte CRCCheck(byte[] buffer)
        {
            int index = 1; // Skip STX
            b_CRC8 = 0;

            while (index < buffer.Length)
            {
                byte b_udata = buffer[index++];

                if (b_udata == ETX)
                {
                    if (index + 1 < buffer.Length)
                    {
                        byte b_crc = (byte)(MakeHex(buffer[index++]) * 16);
                        b_crc += MakeHex(buffer[index]);

                        b_CRC8 = (byte)~b_CRC8; // CRC sent complemented

                        return b_crc == b_CRC8 ? (byte)0 : (byte)1;
                    }
                    else
                    {
                        return 2; // Missing ETX
                    }
                }
                else
                {
                    CRC8char(b_udata);
                }
            }

            return 2; // Missing ETX
        }

        public static byte[] GenerateCRCPayload(byte[] payload)
        {
            // Initialize the CRC
            b_CRC8 = 0;

            // Calculate the CRC for the payload
            foreach (byte b in payload)
            {
                CRC8char(b);
            }

            // Complement the CRC
            b_CRC8 = (byte)~b_CRC8;

            // Convert the CRC to its hex representation (two ASCII characters)
            byte highNibble = (byte)((b_CRC8 >> 4) & 0x0F);
            byte lowNibble = (byte)(b_CRC8 & 0x0F);

            // Convert nibbles to ASCII hex characters
            byte highChar = (byte)(highNibble > 9 ? highNibble + 'A' - 10 : highNibble + '0');
            byte lowChar = (byte)(lowNibble > 9 ? lowNibble + 'A' - 10 : lowNibble + '0');

            // Create a new buffer to hold the payload and the CRC characters
            byte[] bufferWithCRC = new byte[payload.Length + 5];

            //stx
            bufferWithCRC[0] = STX;

            // Copy the payload to the new buffer
            Array.Copy(payload, 0, bufferWithCRC, 1, payload.Length);

            //etx
            bufferWithCRC[payload.Length + 1] = ETX;

            // Append the CRC hex characters to the buffer
            bufferWithCRC[payload.Length + 2] = highChar;
            bufferWithCRC[payload.Length + 3] = lowChar;

            //term
            bufferWithCRC[payload.Length + 4] = TERM;

            return bufferWithCRC;
        }

        private static void CRC8char(byte d)
        {
            b_CRC8 = crc8revtab[b_CRC8 ^ d]; // CRC must be complemented elsewhere
        }

        private static byte MakeHex(byte b)
        {
            if (b >= '0' && b <= '9')
                return (byte)(b - '0');
            if (b >= 'A' && b <= 'F')
                return (byte)(b - 'A' + 10);
            if (b >= 'a' && b <= 'f')
                return (byte)(b - 'a' + 10);

            throw new ArgumentException("Invalid hex character");
        }
        //
    }
}
