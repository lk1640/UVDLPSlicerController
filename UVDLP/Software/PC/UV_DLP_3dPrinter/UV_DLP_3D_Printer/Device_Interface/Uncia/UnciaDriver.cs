﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Ports;
using System.Timers;
using System.Threading;
namespace UV_DLP_3D_Printer.Drivers
{
    /// <summary>
    /// </summary>
    public class UnciaDriver : GenericDriver
    {
        //Timer m_reqtimer;
        //private static double s_interval = 250; // 1/4 second
        public UnciaDriver()
        {
            m_drivertype = eDriverType.eUNCIA; // set correct driver type
         //   m_reqtimer = new Timer();
         //   m_reqtimer.Interval = s_interval;
         //   m_reqtimer.Elapsed += new ElapsedEventHandler(m_reqtimer_Elapsed);
            UVDLPApp.Instance().m_deviceinterface.AlwaysReady = true; // don't looks for gcode responses, always assume we're ready for the next command.
        }

        /// <summary>
        /// override the base class implementation of the connect
        /// so we can start the timer
        /// </summary>
        /// <returns></returns>
        public override bool Connect()
        {
            bool ret = false;
            try
            {
                ret = base.Connect();
                return ret;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance().LogRecord(ex.Message);
                return ret;
            }
        }
        /// <summary>
        /// Override the base class implementation of the disconnect
        /// in order to stop the request status timer
        /// </summary>
        /// <returns></returns>
        public override bool Disconnect()
        {
            try
            {
                bool ret = base.Disconnect();
                
                return ret;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance().LogRecord(ex.Message);
                return false;
            }
        }


        /// <summary>
        /// This function starts looking at the 2 character in the line (index 1)
        /// and will read characters until the whitespace
        /// and return the g/m code
        /// </summary>
        /// <param name="?"></param>
        /// <returns></returns>
        private int GetGMCode(string line)
        {
            try
            {
                int idx = 1;
                string val = "";
                string ss = line.Substring(idx, 1);
                while (ss != " " && ss != "\r" && idx < line.Length)
                {
                    ss = line.Substring(idx++, 1);
                    val += ss;
                }
                int retval = int.Parse(val);
                return retval;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance().LogError(ex);
            }
            return -1;
        }

        private double GetGCodeValDouble(string line, char var)
        {
            try
            {
                // scan the string, looking for the specified var
                // starting at the next position, start reading characters
                // until a space occurs or we reach the end of the line
                double val = 0;
                int idx = line.IndexOf(var);
                if (idx != -1)
                {
                    // found the character
                    //look for the next space or end of line
                    string sval = "";
                    string ss = line.Substring(idx++, 1);
                    while (ss != " " && ss != "\r" && idx < line.Length)
                    {
                        ss = line.Substring(idx++, 1);
                        sval += ss;
                    }
                    val = double.Parse(sval.Trim());
                }
                return val;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance().LogError(ex);
                return 0.0;
            }
        }
        /// <summary>
        /// </summary>
        /// <param name="mm"></param>
        /// <returns></returns>
        private byte CalcZSteps(double mm)
        {
            // the uncia printer moves 100 microns per 300 steps

            double mm2inch = 0.0393701;
            double conv = mm * mm2inch;// convert mm to inch
            double val = conv / .0005;//divide inches by .0005
            byte retval = (byte)Math.Round(val); // hopefully rounding won't be necessary if we've choosen slice height correctly
            return retval;

        }
        /// <summary>
        /// This interprets the gcode/mcode
        /// generates a command, and sends the data to the port
        /// it returns number of bytes written
        /// </summary>
        /// <param name="line"></param>
        private int InterpretGCode(string line)
        {
            try
            {
                int retval = 0;
                string ln = line.Trim(); // trim the line to remove any leading / trailing whitespace
                ln = ln.ToUpper(); // convert to all upper case for easier processing
                int code = -1;
                byte[] cmd;
                cmd = new byte[1];
                //int delay = 1;//0; //slow delay. I think fast delay is 0

                if (ln.StartsWith("G"))
                {
                    code = GetGMCode(line);
                    switch (code)
                    {
                        case -1:// error getting g/mcode
                            DebugLogger.Instance().LogError("Error getting G/M code: " + line);
                            break;
                        case 1: // G1 movement command - 
                            // C
                            double zval = GetGCodeValDouble(line, 'Z');
                            cmd[0] = (byte)'E'; // enable the stepper motor
                            Write(cmd, 1);
                            //Thread.Sleep(delay); // add a delay between steps

                            //convert that to steps - 90 steps per mm
                            double zsteps = zval * 90; // 3000 steps /mm ? // 9 steps = 100 microns = .1mm
                            int iZStep = (int)Math.Abs(zsteps); // get the ABS value
                            if (zval > 0)
                            {
                                cmd[0] = (byte)'F';// forward direction                            
                            }
                            else 
                            {
                                cmd[0] = (byte)'R';
                            }
                            Write(cmd, 1); // write the direction byte
                            //

                            cmd[0] = ( byte)'S';

                            for (int c = 0; c < (int)iZStep; c++) 
                            {
                                Write(cmd, 1);
                              //  Thread.Sleep(delay); // add a delay between steps
                            }
                                
                            break;
                        case 28: // G28 Homing command
                          //  retval = Write(cmd, 8); // send the command
                            break;
                    }
                }
                else if (ln.StartsWith("M"))
                {
                    code = GetGMCode(line);
                    switch (code)
                    {
                        case -1:// error getting g/mcode
                            DebugLogger.Instance().LogError("Error getting G/M code: " + line);
                            break;

                    }
                }
                return retval;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance().LogError(ex);
                return 0; // error writing / decoding
            }
        }
        /// <summary>
        /// We're overriding the read here
        /// We're going to need to listen to the system status messages sent back from the printer.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void m_serialport_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int read = m_serialport.Read(m_buffer, 0, m_serialport.BytesToRead);
            byte[] data = new byte[read];
            for (int c = 0; c < read; c++)
            {
                data[c] = m_buffer[c];
            }
            Log(data, read);
            RaiseDataReceivedEvent(this, data, read);
            // we're also going to have to raise an event to the deviceinterface indicating that we're 
            // ready for the next command, because this is different than the standard
            // gcode implementation where the device interface looks for a 'ok',
            //we'll probably have to also raise a signal to the deviceinterface NOT to look for the ok
            // so it doen't keep adding up buffers.
        }

        public override int Write(String line)
        {
            try
            {
                int sent = 0;
                string tosend = RemoveComment(line);
                lock (_locker) // ensure synchronization
                {
                    line = RemoveComment(line);
                    if (line.Trim().Length > 0)
                    {
                        Log(line);
                        sent = InterpretGCode(line);
                        // interpret the gcode line,
                        // generate the coomand
                        //send the command
                        //m_serialport.Write(line);
                    }
                    return sent;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance().LogError(ex);
                return 0;
            }
        }
    }
}