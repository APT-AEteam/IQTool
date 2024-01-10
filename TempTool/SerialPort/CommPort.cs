using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Net;
using System.Web;
using System.Security.Cryptography;
using System.Globalization;
using System.Windows.Forms;
using System.Linq;

namespace CommPort
{
    class COMPORT
    {
        SerialPort portComm;
        int nPortNo;
        int nBaudRate;
        string sPortName;
        bool bGpsOn;
        private List<DevicePort> lSpecialComPorts;
        string sOutMsg;

        //更新测试项目状态
        public delegate void DelegateUpdateTestStatus(bool result, TestInfo error);
        public DelegateUpdateTestStatus UpdateStatus;
        //更新串口收发日志
        public delegate void DelegateUpdatePortRecord(IQT.mainForm.StrTypeEnum type, byte[] str);
        public DelegateUpdatePortRecord UpdatePortRecord;
        //更新消息日志
        public delegate void DelegateUpdateLogRecord(string logStr);
        public DelegateUpdateLogRecord UpdateLogRecord;


        public int PortNo
        {
            get             //得到
            {
                return nPortNo;  
            }
            set             //设置
            {
                Close();
                nPortNo = value;
                portComm.PortName = "COM" + nPortNo.ToString();
            }
        }
        public string PortName
        {
            get
            {
                return sPortName;
            }
            set
            {
                Close();
                sPortName = value;
                portComm.PortName = sPortName;
            }
        }
        public int BaudRate
        {
            get
            {
                return nBaudRate;
            }
            set
            {
                nBaudRate = value;
                portComm.BaudRate = nBaudRate;
            }
        }

        public bool GPSStatusON
        {
            get
            {
                return bGpsOn;
            }
            set
            {
                bGpsOn = value;
            }
        }

        

        public COMPORT()  //结构函数
        {
            nPortNo = 1;
            portComm = new SerialPort();
            portComm.BaudRate = 9600;
            portComm.PortName = "COM1";
            portComm.ReadBufferSize = 1024 * 1024;
            portComm.WriteBufferSize = 1024 * 1024;
            portComm.NewLine = "\r\n";
            bGpsOn = false;

            lSpecialComPorts = new List<DevicePort>();
            portComm.DataReceived += PortComm_DataReceived;
        }

        static byte[] FrameHeader = { 0xE5, 0x5E };
        static byte[] FrameEnd = { 0xFD, 0xFE };

        enum OperaResultEnum
        {
            SUCCESS = 0x00,
            FAIL = 0x01,
            CONNECTFAIL = 0x02,
            OUTOFRANGE = 0x03,
            CMDERROR = 0x04,
            FRAMEERROR = 0x05,
        }
        
        object lockObj=new object();
        private void PortComm_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int readByteCount = portComm.BytesToRead;
            if (readByteCount < 4)
                return;
            byte[] ReadBuffer = new byte[readByteCount];
            portComm.Read(ReadBuffer, 0, ReadBuffer.Length);
            //打印串口接收日志
            UpdatePortRecord(IQT.mainForm.StrTypeEnum.RECV, ReadBuffer);
            if (FrameHeader[0] != ReadBuffer[0] || FrameHeader[1] != ReadBuffer[1])
            {
                IQT.mainForm.TestDone = true;
                return;
            }
            byte OperationResult = ReadBuffer[2];
            if (0x00 != OperationResult)
            {
                if (0x02 == OperationResult)//芯片连接异常
                {
                    UpdateLogRecord("连接异常");
                    IQT.mainForm.ReTest = true;
                }
                else
                    IQT.mainForm.ReTest = false;
                IQT.mainForm.TestDone = true;
                return;
            }
            byte[] addressBytes = ReadBuffer.Skip(3).Take(4).ToArray();
            byte dataLength = ReadBuffer[7];//返回数据长度
            if (0x00 != dataLength)
            {
                byte[] readBack = ReadBuffer.Skip(8).Take(dataLength).Reverse().ToArray();
                Monitor.Enter(lockObj);
                DecodeExpection(readBack);
                UpdateStatus(TestResult, result);
                IQT.mainForm.ReTest = true;
                Monitor.Exit(lockObj);
            }
            //UpdateStatus("全部关闭成功");
        }

        public static string ErrorTypeString { get; set; }

        static string ByteToHexStr(byte[] bytes)
        {
            string returnStr = "";
            if (bytes != null)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    returnStr += bytes[i].ToString("X2");
                }
            }
            return returnStr;
        }

        void DecodeExpection(byte[] value)
        {
            result = new TestInfo();
            UInt32 exceptValue = UInt32.Parse(ByteToHexStr(value), NumberStyles.HexNumber);
            Console.WriteLine( string.Format("Value: 0x{0}", exceptValue.ToString("X2")));
            result.TRIMValue = (UInt32.Parse(ByteToHexStr(value), NumberStyles.HexNumber) >> 0) & 0x1ffff800;
            if (result.TRIMValue != 0)
            {
                //IQT.mainForm.TrimFirstExceptionTime = DateTime.Now;
                TestInfo.TRIMExceptionFirstTime = DateTime.Now;
            }
            result.IPValue = (UInt32.Parse(ByteToHexStr(value), NumberStyles.HexNumber)) & 0x7ff;
            if((result.IPValue != 0))
            {
                TestInfo.IPExceptionFirstTime = DateTime.Now;
            }

            for (int i = 0; i < value.Length; i++)
            {
                byte exceptionValue = value[i];
                if (exceptionValue != 0x00)
                {
                    switch (i) {
                        case 0:
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.EFLASH % 8)))
                            {
                                result.EFLASH.Exception = true;
                                TestInfo.EFLASHExceptionFirstTime = DateTime.Now;
                                IQT.mainForm.EFLASHFirstExceptionTime = DateTime.Now;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.PROM_READ % 8)))
                            {
                                result.ROM.Exception = true;
                                if (!result.ROMErrDict.ContainsKey("PROM"))
                                    result.ROMErrDict.Add("PROM", DateTime.Now);
                                TestInfo.PROMExceptionFirstTime = DateTime.Now;
                                //result.ROM.FirstTime = DateTime.Now;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.DROM_READ % 8)))
                            {
                                result.ROM.Exception = true;
                                if (!result.ROMErrDict.ContainsKey("DROM"))
                                    result.ROMErrDict.Add("DROM", DateTime.Now);
                                TestInfo.DROMExceptionFirstTime = DateTime.Now;
                                //result.ROM.FirstTime = DateTime.Now;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.OPAP % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.OPAN % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.FT % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            break;
                        case 1:
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.CP3 % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.CP2 % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.FCP % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.BUF % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.OSC5 % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.ADC_OFC % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.FVR % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.ISOSC % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            break;
                        case 2:
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.HF_EF % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.IMOSC % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.BGR_CMR % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.UID % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.REV_CODE % 8)))
                            {
                                result.TRIM.Exception = true;
                            }
                            break;
                        case 3:
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.CAN % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if(!result.IPErrDict.ContainsKey("CAN"))
                                    result.IPErrDict.Add("CAN", DateTime.Now);
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.RTC % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if(!result.IPErrDict.ContainsKey("RTC"))
                                    result.IPErrDict.Add("RTC", DateTime.Now);
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.LPT % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if(!result.IPErrDict.ContainsKey("LPT"))
                                    result.IPErrDict.Add("LPT", DateTime.Now);
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.BT % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if(!result.IPErrDict.ContainsKey("BT"))
                                    result.IPErrDict.Add("BT", DateTime.Now);
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.GPIO % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if(!result.IPErrDict.ContainsKey("GPIO"))
                                    result.IPErrDict.Add("GPIO", DateTime.Now);
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.USART % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if(!result.IPErrDict.ContainsKey("USART"))
                                    result.IPErrDict.Add("USART", DateTime.Now);
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.UART % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if(!result.IPErrDict.ContainsKey("UART"))
                                    result.IPErrDict.Add("UART", DateTime.Now);
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.ADC % 8)))
                            {
                                result.IP.Exception = true;
                                //TestInfo.IPExceptionFirstTime = DateTime.Now;
                                if (!result.IPErrDict.ContainsKey("ADC"))
                                    result.IPErrDict.Add("ADC", DateTime.Now);
                            }
                            break;
                    }
                }

                Thread.Sleep(100);
            }
           
        }
        public static bool TestResult = false;
        public static TestInfo result;
        public class TestInfo
        {
            public TestInfo()
            {
                EFLASH = new ResultInfo();
                IP = new ResultInfo();
                ROM= new ResultInfo();
                TRIM = new ResultInfo();
                ROMErrDict = new Dictionary<string, DateTime>();
                ROMErrDict.Clear();
                //ROMErrDict.Clear();
                TRIMValue = 0;
                IPValue = 0;
                IPErrDict = new Dictionary<string, DateTime>();
                IPErrDict.Clear();
                //IPErrDict.Clear();
                //EFLASHExceptionFirstTime = DateTime.MaxValue;
                //PROMExceptionFirstTime = DateTime.MaxValue;
                //DROMExceptionFirstTime = DateTime.MaxValue;
                //TRIMExceptionFirstTime = DateTime.MaxValue;
            }
            private static DateTime eflashExceptionFirstTime = DateTime.MaxValue;
            public static DateTime EFLASHExceptionFirstTime{
                get {   return eflashExceptionFirstTime; }
                set
                {
                    if (value < EFLASHExceptionFirstTime& value!= DateTime.MinValue) { 
                        eflashExceptionFirstTime = value;
                    }
                }
            }
            private static DateTime promExceptionFirstTime = DateTime.MaxValue;
            public static DateTime PROMExceptionFirstTime
            {
                get { return promExceptionFirstTime; }
                set
                {
                    if (value < PROMExceptionFirstTime& value!= DateTime.MinValue) { 
                        promExceptionFirstTime = value;
                    }
                }
            }
            private static DateTime dromExceptionFirstTime = System.DateTime.MaxValue;
            public static DateTime DROMExceptionFirstTime
            {
                get { return dromExceptionFirstTime; }
                set
                {
                    if (value < DROMExceptionFirstTime & value != DateTime.MinValue)
                    {
                        dromExceptionFirstTime = value;
                    }
                }
            }
            private static DateTime trimExceptionFirstTime = DateTime.MaxValue;
            public static DateTime TRIMExceptionFirstTime
            {
                get { return trimExceptionFirstTime; }
                set
                {
                    if (value < TRIMExceptionFirstTime & value != DateTime.MinValue)
                    {
                        trimExceptionFirstTime = value;
                    }
                }
            }
            private static DateTime ipExceptionFirstTime = DateTime.MaxValue;
            public static DateTime IPExceptionFirstTime
            {
                get { return ipExceptionFirstTime; }
                set
                {
                    if (value < IPExceptionFirstTime & value != DateTime.MinValue)
                    {
                        ipExceptionFirstTime = value;
                    }
                }
            }



            public int BoardNumber { get; set; } 
            public ResultInfo EFLASH { get; set; } 
            public ResultInfo ROM { get; set; } 
            public ResultInfo TRIM { get; set; } 
            public ResultInfo IP { get; set; }
            public  Dictionary<string, DateTime> ROMErrDict = new Dictionary<string, DateTime>();
            public  UInt32 TRIMValue { get; set; }
            public  UInt32 IPValue { get; set; }
            public  Dictionary<string, DateTime> IPErrDict=new Dictionary<string, DateTime>();
        }
        public class ResultInfo
        {
            public bool Exception { get; set; }
            private DateTime firstTime;
            public DateTime FirstTime {
                get { return firstTime; }
                set
                {
                    if (value < FirstTime) { 
                        firstTime = value;
                    }
                }
            }
            public ResultInfo()
            {
                Exception = false;
                FirstTime = DateTime.MaxValue;
            }
        }

        enum ExceptionType
        {
            IP,
            ROM,
            TRIM,
            EFLASH
        }

        enum ExceptionBitMask
        {
            //IP异常
            ADC = 0x00,
            UART = 0x01,
            USART = 0x02,
            GPIO = 0x03,
            BT = 0x04,
            LPT = 0x05,
            RTC = 0x06,
            CAN = 0x07,
                     
            //TRIM异常
            REV_CODE = 0x0B,
            UID = 0x0C,
            BGR_CMR = 0xD,
            IMOSC = 0x0E,
            HF_EF = 0x0F,
            ISOSC = 0x10,
            FVR = 0x11,
            ADC_OFC = 0x12,
            OSC5 = 0x13,
            BUF = 0x14,
            FCP = 0x15,
            CP2 = 0x16,
            CP3 = 0x17,
            FT = 0x18,
            OPAN = 0x19,
            OPAP = 0x1A,

            //ROM(DROM/PROM)
            DROM_READ = 0x1D,
            PROM_READ = 0x1E,

            //EFLASH校验异常
            EFLASH = 0x1F,
        }

        ~COMPORT()
        {
            Stop();
            portComm = null;
        }

        public bool SendData(byte[] data ,ref string errMsg)
        {
            if (!Start())
            {
                errMsg = "请先选择串口";
                return false;
            }
            try
            {
                portComm.Write(data, 0, data.Length);
                UpdatePortRecord(IQT.mainForm.StrTypeEnum.SEND, data);
            }
            catch (Exception)
            {
                errMsg = "串口发送失败";
                return false;
            }
            return true;
        }

        public bool Open()   //打开串口
        {
            Close();
            try
            {         
                portComm.Open();
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public void Close()
        {
            if (portComm.IsOpen)
            {
                try
                {
                    portComm.Close();
                }
                finally
                {
                  
                    portComm.Dispose();
                }
            }
       
        }



        //public string ReadAtCommand()
        //{
        //    sOutMsg = "";
        //    if (portComm.IsOpen)
        //    {
        //        try
        //        {
        //            Thread.Sleep(50);
        //            sOutMsg = portComm.ReadExisting().ToString();
        //            mainFrm.SendComMsg(sOutMsg);
        //            return sOutMsg;
        //        }
        //        catch
        //        {
        //            MessageBox.Show(sOutMsg);
        //            return "";
        //        }
        //    }
        //    else
        //        return "";
        //}

        //public string ReadAtLineCommand()
        //{
        //    sOutMsg = "";
        //    if (portComm.IsOpen)
        //    {
        //        try
        //        {
        //            Thread.Sleep(50);
        //            sOutMsg = portComm.ReadLine();
        //            mainFrm.SendComMsg(sOutMsg);
        //            return sOutMsg;
        //        }
        //        catch
        //        {
        //            MessageBox.Show(sOutMsg);
        //            return "";
        //        }
        //    }
        //    else
        //        return "";
        //}

        //public string ReadAtCommandgsensor()
        //{
        //    sOutMsg = "";
        //    if (portComm.IsOpen)
        //    {
        //        try
        //        {
        //            Thread.Sleep(1000);
        //            sOutMsg = portComm.ReadExisting().ToString();
        //            mainFrm.SendComMsg(sOutMsg);
        //            return sOutMsg;
        //        }
        //        catch
        //        {
        //            MessageBox.Show(sOutMsg);
        //            return "";
        //        }
        //    }
        //    else
        //        return "";
        //}

        //public string ReadGPSInfo()
        //{
        //    sOutMsg = "";
        //    if (portComm.IsOpen)
        //    {
        //        try
        //        {
        //            sOutMsg = portComm.ReadExisting().ToString();
        //            return sOutMsg;
        //        }
        //        catch
        //        {
        //            // MessageBox.Show(sOutMsg);
        //            return "";
        //        }
        //        finally
        //        {
        //            mainFrm.SendComMsg(sOutMsg);
        //            portComm.DiscardInBuffer();
        //            Thread.Sleep(2000);
        //        }
        //    }
        //    else
        //        return "";
        //}
        //public bool SendStr(string sData)
        //{
        //    try
        //    {
        //        portComm.DiscardInBuffer();
        //        Thread.Sleep(200);
        //        portComm.Write(sData);
               
        //    }
        //    catch (Exception)
        //    {
                
        //        return false;
        //    }

        //    return true;
        //}

        //public bool SendWIFIStr(string sData)
        //{
        //    try
        //    {
        //        portComm.DiscardInBuffer();
        //        Thread.Sleep(200);
        //        portComm.Write(sData);

        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

        //    return true;
        //}

        public bool Start()  //串口打开返回true,否则false；
        {
            if (!Open())
                return false;

            return true;
        }

        public void Stop()
        {
            Close();
        }

        /// <summary> 
        /// 连接DUT
        /// </summary>  
        /// <param>
        /// inComPort,端口号
        /// inBaudRate，波特率
        /// inTimeout，连接超时，ms
        /// </param> 
        public bool Connect2DUT(int inComPort, int inBaudRate, int inTry)
        {
            bool bStatus = false;
            int initTimes = inTry;//USB连接的次数
            PortNo = inComPort;
            BaudRate = inBaudRate;
            try
            {
                do
                {
                    initTimes--;
                    bStatus = Start();
                    if (!bStatus)
                    {
                        if (initTimes <= 0)
                            return bStatus;
                        Close();
                        Thread.Sleep(500);
                        continue;
                    }

                } while (initTimes > 0 && !bStatus);
            }
            catch (Exception)
            {

                Close();
                return false;
            }


            return true;
        }

        /// <summary> 
        /// 扫描DUT端口
        /// </summary>  
        /// <param>
        /// iScanTimeOut,扫描限时，如50s.
        /// iEndAfterScanned，连续扫描到几次算OK，如4次.
        /// bEndIfScanned，是否扫描OK就开始测试,否则等限时到时再开始.
        /// </param> 
        public bool ScanComPort(int iScanTimeOut, int iEndAfterScanned, int DelayTime)
        {
            int iScanTime = iScanTimeOut;//扫描限时
            int iDelayTime = DelayTime;
            int iComportFound = 0;
            do
            {
                iScanTime--;
                Thread.Sleep(10);
                lSpecialComPorts = EnumComPort.GetSpecialPortNames("MTK","");
               // lSpecialComPorts = EnumComPort.GetSpecialPortNames(SerialName);
                if (lSpecialComPorts.Count == 0)
                {
                    iComportFound = 0;
                    continue;
                }
                else
                {
                    iComportFound++;
                }
                //Application.DoEvents();
            } while ((iScanTime >= 0) && (iComportFound < iEndAfterScanned));

            if (iScanTime <= 0)
            {
                return false;
            }
            else
            {
                while (DelayTime > 0)
                {
                    DelayTime--;
                    Thread.Sleep(10);
                }
            }

            PortName = lSpecialComPorts[0].PortName;
           
            return true;
        }
        public bool SendStr(string sData)
        {
            try
            {
                
                portComm.DiscardInBuffer();
                Thread.Sleep(200);
                portComm.Write(sData);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return false;
            }

            return true;
        }

        /// <summary> 
        /// 进入测试模式
        /// </summary>  
        //public bool EnterTestMode()
        //{
          
        //    bool RESULT = true; ;
        //    string sCmd = "AT^TESTSTART\n";
        //    if (!SendStr(sCmd))
        //    {
        //        RESULT = false;
        //        return RESULT;
        //    }
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("OK"))
        //    {
        //        RESULT = false;
        //        return RESULT;
        //    }

        //    return RESULT;
        //}

        /// <summary> 
        /// 退出测试模式
        /// </summary> 
        //public bool ExitTestMode()
        //{
        //    string sCmd = "AT^TESTEND\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}

        /// <summary> 
        /// 写入硬码
        /// </summary> 
        //public bool HardCodeWrite(string inHardcode)
        //{
        //    string sCmd = "";

        //    sCmd = string.Format("AT^HARDCODE={0}\n", inHardcode);
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}

        /// <summary> 
        /// 写入SN号
        /// </summary> 
        //public bool SNCodeWrite(string inSN)
        //{
        //    string sCmd = "";
        //    bool GoTo = false;
        //    sCmd = string.Format("AT+EGMR=1,5,\"{0}\"\n", inSN);
        //    if (!SendStr(sCmd))
        //        return false;
        //    //Thread.Sleep(1500);
        //    for (int i = 0; i < 3; i++)
        //    {
        //        Thread.Sleep(1000);
        //        if (ReadAtCommand().Contains("OK"))
        //        {
        //            GoTo = true;
        //            break;
        //        }
        //    }
        //    return GoTo;
        //    //if (GoTo)
        //    //{
        //    //    return true;
        //    //}
        //    //else
        //    //    return false;
        //}

        /// <summary> 
        /// 读取SN号
        /// </summary> 
        //public bool GetSNCode(ref string outSN)
        //{
        //    bool result = true;
        //    string sCmd = "AT+EGMR=0,5\n";
        //    string[] strArray1 = { "+EGMR:" };
        //    string[] strArray2 = { "\r\n" };

        //    if (!SendStr(sCmd))
        //    {
        //        result = false;
        //        return result;
        //    }
        //    Thread.Sleep(1500);
        //    string temStr = ReadAtCommand();
        //    if (!temStr.Contains("OK"))
        //    {
        //        result = false;
        //        return result;
        //    }

        //    outSN = temStr.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Replace("\"", "").Trim();

        //    return result;
        //}

        /// <summary> 
        /// 写入IMEI号
        /// </summary> 
        //public bool IMEICodeWrite(string inImei)
        //{
        //    string sCmd = "";
        //    bool result = false;
        //    sCmd = string.Format("AT+EGMR=1,7,\"{0}\"\n", inImei);

        //    for (int i = 0; i < 3; i++)
        //    {
        //        if (!SendStr(sCmd))
        //            result = false;

        //        Thread.Sleep(800);
        //        if (ReadAtCommand().Contains("OK"))
        //        {
        //            result = true ;
        //            break;
        //        }
        //    }
        //    return result;
        //}
        /// <summary> 
        /// 读取IMEI号
        /// </summary> 
        //public bool GetIMEICode(ref string outImei)
        //{
        //    string sCmd = "AT+EGMR=0,7\n";
        //    string[] strArray1 = { "+EGMR:" };
        //    string[] strArray2 = { "\r\n" };

        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    string temStr = ReadAtCommand();
        //    if (!temStr.Contains("OK"))
        //        return false;

        //    outImei = temStr.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Replace("\"", "").Trim();

        //    return true;
        //}
        /// <summary> 
        /// 写入Qrcode
        /// </summary> 
        //public bool QrCodeWrite(string inQrCode)
        //{
        //    string sCmd = "";

        //    sCmd = string.Format("AT^QRUPDATE={0}\n", inQrCode);
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}

        //public bool bleWrite(string blecode)
        //{
        //    string sCmd = "";

        //    sCmd = string.Format("AT^SETBLEMAC={0}\n", blecode);
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}

        //public bool bleRead(out string btmac)
        //{
        //    string sCmd = "AT^GETBLEMAC\n";
        //    string receivemsg = "";
        //    btmac = "";

        //    string[] strArray1 = { "BLEMac:<" };
        //    string[] strArray2 = { ">" };
          
        //    //if (sBlefw == "")
        //    //{
        //    //    return false;
        //    //}
        //    //sCmd = string.Format("AT^SETBLEMAC={0}\n", blecode);
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    receivemsg = ReadAtCommand();
        //    if (!receivemsg.Contains("OK"))
        //        return false;
        //    else
        //    {
                
        //        btmac = receivemsg.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Trim().ToUpper();
        //    }

        //    return true;
        //}
        /// <summary> 
        /// 关机，仅关闭GSM系统
        /// </summary>  
        //public bool PowerOff()
        //{
        //    string sCmd = "AT^PWROFF\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}


        /// <summary> 
        /// 全部关机，包括BLE
        /// </summary> 
        //public bool WatchOff()
        //{
        //    string sCmd = "AT^RESET\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    sCmd = "AT^WATCHOFF\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}
     

        /// <summary> 
        /// 存储模式
        /// </summary> 
        //public bool USB()
        //{
        //    string sCmd = "AT^USBMASSSTORAGE\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(200);
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}
        /// <summary> 
        /// GSM关机,BLE进入深度休眠
        /// </summary> 
        //public bool WatchSleep()
        //{
        //    string sCmd = "AT^WATCHSLEEP\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}
        /// <summary>
        /// 检查手表的表情包，勋章包，故事包是否正确无误
        /// </summary>
        /// <returns></returns>
        //public bool ExpPackageCheck()
        //{
        //    string sCmd = "AT^TALE=package_verify\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("Package files test ok"))
        //        return false;
        //    return true;
        //}
        //public bool ICCIDCheck()
        //{
        //    string info = "";
        //    string sCmd = "AT^INFO\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(700);
        //    info = ReadAtCommand();
        //    if (!info.Contains("OK"))
        //        return false;
        //    string sIccid = "";
        //    string[] strArray1 = { "SW_VERNO1=" };
        //    string[] strArray2 = { "\r\n" };
        //    strArray1[0] = "SIM_ICCID=";
        //    sIccid = info.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //    sIccid = sIccid.Replace(";", "B");
        //    sIccid = sIccid.Replace("?", "");
        //    if (sIccid == "")
        //    {
        //        return false;
        //    }
        //    return true;
        //}

        //public bool ICCIDCheckW607(out string sIccidStatus, out string sIccid)
        //{
        //    string info = "";
        //    string sCmd = "AT^INFO\n";
        //    if (!SendStr(sCmd))
        //    {
        //        sIccid = "";
        //        sIccidStatus = "";
        //        return false;
        //    }
        //    Thread.Sleep(1500);
        //    info = ReadAtCommand();
        //    if (!info.Contains("OK"))
        //    {
        //        sIccid = "";
        //        sIccidStatus = "";
        //        return false;
        //    }
        //    //string sIccid = "";
        //    //string sIccidStatus = "";
        //    string[] strArray1 = { "SIM_CARD_STATUS=" };
        //    string[] strArray2 = { "\r\n" };
        //    string[] strArray3 = { "SIM_ICCID=" };
        //    //strArray1[0] = "SIM_ICCID=";
        //    sIccidStatus = info.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].TrimEnd().TrimStart();
        //    sIccid = info.Split(strArray3, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //    //sIccid = sIccid.Replace(";", "B");
        //    //sIccid = sIccid.Replace("?", "");

        //    if (sIccid != "" && sIccidStatus == "1")
        //    {
        //        return true;
        //    }
        //    else
        //        return false;
            
        //}

        //public bool WriteWifiMac(string station,string Wifimac)
        //{
        //    string info = "";
        //    string sCmd = "";
        //    if (station == "QB001" || station == "QB002")
        //    {
        //        sCmd = "AT+WIFIMAC=" + Wifimac + "\n";
        //    }
        //    else
        //    {
        //        sCmd = "AT^SETWIFIMAC=" + Wifimac + "\n";
        //    }
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(1000);
        //    info = ReadAtCommand();
        //    if (!info.ToUpper ().Contains("OK"))
        //        return false;
          
        //    return true;
        //}

        //public bool ReadWifiMac(ref string wifimac)
        //{
        //    string info = "";
        //    string sCmd = "AT+GETWIFIMAC";
        //    string[] strArray1 = { "+GETWIFIMAC:" };
        //    string[] strArray2 = { "\r\n" };
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(700);
        //    info = ReadAtCommand();
        //    if (!info.ToUpper().Contains("OK"))
        //        return false;
        //    wifimac = info.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Replace("\"", "").Trim().ToUpper ();
        //    return true;
        //}
        /// <summary> 
        /// 获取产品信息
        /// </summary> 
        //public bool GetProductInfo(out string info)
        //{
        //    info = "";
        //    string sCmd = "AT^INFO\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(1000);
        //    info = ReadAtCommand();
        //    if (!info.Contains("OK"))
        //        return false;

        //    return true;
        //}

        //public bool GetProductInfo()
        //{
        //    //info = "";
        //    string sCmd = "AT^INFO\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //Thread.Sleep(1000);
        //    //info = ReadAtCommand();
        //    //if (!info.Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool GetProductInfo(string sModel, out string sGFWVer, out string sBleFWVer, out string sImei, out string sHardcode, out string sIccid, out string sWifiMic, out string sQrCode)
        //{
        //    string strRec = "", temModel = "";
        //    sGFWVer = "";
        //    sBleFWVer = "";
        //    sImei = "";
        //    sHardcode = "";
        //    sIccid = "";
        //    sWifiMic = "";
        //    sQrCode = "";

        //    try
        //    {
        //        if (!GetProductInfo(out strRec))
        //            return false;
        //        if (sModel.ToUpper() == "W362" || sModel.ToUpper() == "W461B" || sModel.ToUpper() == "W461C")
        //        {
        //            temModel = "W461";
        //        }
        //        else if (sModel.ToUpper() == "W4B")
        //        {
        //            temModel = "W4";
        //        }
        //        else
        //        {
        //            temModel = sModel.ToUpper();
        //        }
        //        switch (temModel)
        //        {
        //            case "W361":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];

        //                    strArray1[0] = "SW_VERNO2=";
        //                    sBleFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1[0] = "SIM_ICCID=";
        //                    sIccid = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sIccid = sIccid.Replace(";", "B");
        //                    sIccid = sIccid.Replace("?", "");

        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    if (!GetWifiMac(out sWifiMic))
        //                    {
        //                        return false;
        //                    }
        //                }
        //                break;
        //            case "W461":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];

        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1[0] = "SIM_ICCID=";
        //                    sIccid = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sIccid = sIccid.Replace(";", "B");
        //                    sIccid = sIccid.Replace("?", "");

        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    if (!GetBleFw(out sBleFWVer))
        //                    {
        //                        return false;
        //                    }
        //                    if (!GetWifiMac(out sWifiMic))
        //                    {
        //                        return false;
        //                    }
        //                }
        //                break;
        //            case "W461BHW":
        //                {
        //                    string[] strArray1 = { "SW_VERSION=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    if (!GetBleFw(out sBleFWVer))
        //                    {
        //                        return false;
        //                    }
        //                    if (!GetWifiMac(out sWifiMic))
        //                    {
        //                        return false;
        //                    }
        //                }
        //                break;
        //            case "W4":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];

        //                    strArray1[0] = "SW_VERNO2=";
        //                    sBleFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1[0] = "SIM_ICCID=";
        //                    sIccid = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sIccid = sIccid.Replace(";", "B");
        //                    sIccid = sIccid.Replace("?", "");

        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    if (!GetWifiMac(out sWifiMic))
        //                    {
        //                        return false;
        //                    }
        //                }
        //                break;
        //            case "4C":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    //strArray1[0] = "null";
        //                    //sIccid = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    //sIccid = sIccid.Replace(";", "B");
        //                    //sIccid = sIccid.Replace("?", "");

        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    for (int i = 0; i < 3; i++)
        //                    {
        //                        if (!GetWifiMac(out sWifiMic))
        //                        {
        //                            if (i == 2)
        //                            { return false; }
        //                        }
        //                        else
        //                        { i = 2; }
        //                    }
                           

        //                }
        //                break;
        //            case "W602":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    if (!GetWifiMac(out sWifiMic))
        //                    {

        //                        return false;
        //                    }

        //                }
        //                break;
        //            case "QB001": //FW版本,IMEI CODE ,HARD CODE,QR CODE,WIFI MAC
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];  //FW版本
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0]; //IMEI
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0]; //HARDCODE

        //                    //strArray1[0] = "null";
        //                    //sIccid = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    //sIccid = sIccid.Replace(";", "B");
        //                    //sIccid = sIccid.Replace("?", "");

        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];  //QR码

        //                    for (int i = 0; i < 3; i++)
        //                    {
        //                        if (!GetWifiMac(out sWifiMic))
        //                        {
        //                            if (i == 2)
        //                            { return false; }
        //                        }
        //                        else
        //                        { i = 2; }
        //                    }
        //                }
        //                break;
        //            case "QB002": //FW版本,IMEI CODE ,HARD CODE,QR CODE,WIFI MAC
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];  //FW版本
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0]; //IMEI
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0]; //HARDCODE

        //                    //strArray1[0] = "null";
        //                    //sIccid = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    //sIccid = sIccid.Replace(";", "B");
        //                    //sIccid = sIccid.Replace("?", "");

        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];  //QR码

        //                    for (int i = 0; i < 3; i++)
        //                    {
        //                        if (!GetWifiMac(out sWifiMic))
        //                        {
        //                            if (i == 2)
        //                            { return false; }
        //                        }
        //                        else
        //                        { i = 2; }
        //                    }
        //                }
        //                break;
        //            case "QB003":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];




        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    //if (!GetBleFw(out sBleFWVer))
        //                    //{
        //                    //    return false;
        //                    //}

        //                    if (!GetWifiMac(out sWifiMic))
        //                    {

        //                        return false;
        //                    }
        //                }


        //                break;
        //            case "QB004":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];




        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    //if (!GetBleFw(out sBleFWVer))
        //                    //{
        //                    //    return false;
        //                    //}

        //                    if (!GetWifiMac(out sWifiMic))
        //                    {

        //                        return false;
        //                    }
        //                }
        //                break;
        //            case "QB005":
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];
        //                    strArray1[0] = "IMEI=";
        //                    sImei = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    strArray1.SetValue("HARDCODE=", 0);
        //                    sHardcode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];




        //                    strArray1[0] = "QR=";
        //                    sQrCode = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];

        //                    //if (!GetBleFw(out sBleFWVer))
        //                    //{
        //                    //    return false;
        //                    //}

        //                    if (!GetWifiMac(out sWifiMic))
        //                    {

        //                        return false;
        //                    }
        //                }

        //                break;
        //            default:
        //                return false;
        //        }

        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

        //    return true;
        //}

        //public bool GetPCBAProductInfo(string sModel, out string sGFWVer)
        //{
        //    string strRec = "", temModel = "";
        //    sGFWVer = ""; 
        //    try
        //    {
        //        if (!GetProductInfo(out strRec))
        //            return false;
               
        //            temModel = sModel.ToUpper();
                
        //        switch (temModel)
        //        {
        //            case "QB001": //GSM FW版本
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];  //FW版本
        //                }
        //                break;
        //            case "QB002": //GSM FW版本
        //                {
        //                    string[] strArray1 = { "SW_VERNO1=" };
        //                    string[] strArray2 = { "\r\n" };
        //                    sGFWVer = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //                    sGFWVer = sGFWVer.Split(new string[] { " " }, StringSplitOptions.None)[0];  //FW版本
        //                }
        //                break;
        //            default:
        //                return false;
        //        }

        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

        //    return true;
        //}

        //public bool GetBleFw(out string sBlefw)
        //{
        //    string strRec = "";
        //    sBlefw = "";
        //    SendStr("AT^ENBLEFM\n");
        //    string sCmd = "AT^BLEVER\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(1000);
        //    strRec = ReadAtCommand();
        //    if (!strRec.Contains("OK"))
        //        return false;

        //    string[] strArray1 = { "BTVer:<" };
        //    string[] strArray2 = { ">" };
        //    sBlefw = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Trim();
        //    if (sBlefw == "")
        //    {
        //        return false;
        //    }
        //    SendStr("AT^DISBLEFM\n");
        //    return true;
        //}

        //public bool GetGsmNet(string sModel, out string sGsmNetNo, out string curDt, out bool GSMsERVER)
        //{
        //    GSMsERVER = false;
        //    string strRec = "";
        //    sGsmNetNo = "";
        //    curDt = "";
        //    try
        //    {
        //        if (!GetProductInfo(out strRec))
        //        {
        //            return false;
        //        }

        //        string[] strArray1 = { "GSM_SERVICE=" };
        //        string[] strArray2 = { "\r\n" };
        //        sGsmNetNo = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Trim();
        //        if (sGsmNetNo == "")
        //        {
        //            return false;
        //        }
        //        if (sModel.ToUpper() == "W362" || sModel.ToUpper() == "W461" || sModel == "W461C" || sModel == "4C" || sModel == "W602" || sModel.ToUpper() == "QB001" || sModel.ToUpper() == "QB002" || sModel.ToUpper() == "QB003" || sModel.ToUpper() == "QB004" || sModel.ToUpper() == "QB005")
        //        {
        //            strArray1[0] = "current dt str:";
        //            curDt = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Trim();
        //            if (curDt == "")
        //            {
        //                return false;
        //            }
        //        }
        //        if (sModel.ToUpper() == "QB001" || sModel.ToUpper() == "QB002" || sModel.ToUpper() == "QB003" || sModel.ToUpper() == "QB004" || sModel.ToUpper() == "QB005")
        //        {
        //            if(strRec.Contains ("GSM_SERVICE_OK"))
        //            {
        //                GSMsERVER = true;
        //            }
        //        }

        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }
        //    return true;
        //}
        //public bool Check_SMS()
        //{
        //    string strRec = "";
        //    string sms_status = "";
        //    string sCmd = "AT^SMSTEST\n";
        //    try
        //    {
        //        if (!SendStr(sCmd))
        //            return false;
        //        strRec = ReadAtCommand();
        //        if (strRec.Contains("ERROR"))
        //            return false;

        //        string[] strArray1 = { "sms test result : " };
        //        string[] strArray2 = { "\r\n" };
        //        sms_status = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //        if (Convert.ToInt32(sms_status) != 1)
        //            return false;
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

        //    return true;
        //}
        //public bool LCD_ON()  //w2
        //{
        //    string sCmd = "AT^LCDTEST\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}

        //public bool WIFIVER_ON()  
        //{
        //    string sCmd = "AT^WIFIVER\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}

        //public bool LCD_TestStart()  //w461c
        //{
        //    string sCmd = "AT^LCDTESTSTART\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool LCD_TestStop()  //w461c
        //{

        //    string sCmd = "AT^LCDTESTSTOP\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //Thread.Sleep(100);
        //    if (!SendStr(sCmd))
        //        return false;

        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool LCDWhite() //w362,w461c
        //{
        //    string sCmd = "AT^LCDWHITE\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool LCDBlack() //w362,w461c
        //{
        //    string sCmd = "AT^LCDBLACK\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool LCDRed() //w461c
        //{
        //    string sCmd = "AT^LCDRED\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool LCDGreen() //w461c
        //{
        //    string sCmd = "AT^LCDGREEN\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool LCDBlue() //w461c
        //{
        //    string sCmd = "AT^LCDBLUE\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}

        //public bool TP_TestStart()  //w461c
        //{
        //    string sCmd = "AT^TPKEYTESTSTART\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool TP_TestStop()  //w461c
        //{
        //    string sCmd = "AT^TPKEYTESTSTOP\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool TP_PressTest(string ModelName, int inTry)
        //{
        //    int itrys = inTry;
        //    string rsMsg = "";
        //    bool TPkey1 = false, TPkey2 = false, TPkey3 = false, TPkey4 = false, TPkey5 = false;
        //    //if (ModelName.ToUpper() != "W461C")
        //    //{
        //    //    return false;
        //    //}
        //    FormTPTest tpFrm = null;
        //    new Thread((ThreadStart)delegate
        //    {
        //        tpFrm = new FormTPTest();
        //        Application.Run(tpFrm);
        //    }).Start();
        //    while (tpFrm == null)
        //        Thread.Sleep(100);
        //    while (itrys >= 0)
        //    {
        //        tpFrm.Invoke((EventHandler)delegate { tpFrm.label1.Text = "TP点按测试，请点按屏幕按钮开始..." + itrys.ToString(); });
        //        Thread.Sleep(1000);
        //        rsMsg = ReadAtCommand();  //触摸时，接收的字符串；
        //        if (rsMsg.Contains("tp key test failed"))
        //        {
        //            tpFrm.Invoke((EventHandler)delegate
        //            {
        //                tpFrm.Close();
        //                tpFrm.Dispose();
        //            });
        //            return false;
        //        }

        //        if (!TPkey1)
        //        {
        //            if (rsMsg.Contains("tp key1 test pass"))
        //            {
        //                TPkey1 = true;
        //                tpFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpFrm.bt_TP1.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (!TPkey2)
        //        {
        //            if (rsMsg.Contains("tp key2 test pass"))
        //            {
        //                TPkey2 = true;
        //                tpFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpFrm.bt_TP2.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (!TPkey3)
        //        {
        //            if (rsMsg.Contains("tp key3 test pass"))
        //            {
        //                TPkey3 = true;
        //                tpFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpFrm.bt_TP3.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (!TPkey4)
        //        {
        //            if (rsMsg.Contains("tp key4 test pass"))
        //            {
        //                TPkey4 = true;
        //                tpFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpFrm.bt_TP4.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (!TPkey5)
        //        {
        //            if (rsMsg.Contains("tp key5 test pass"))
        //            {
        //                TPkey5 = true;
        //                tpFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpFrm.bt_TP5.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (TPkey1 && TPkey2 && TPkey3 && TPkey4 && TPkey5)
        //            break;

        //        itrys--;
        //    }
        //    if (itrys <= 0)
        //    {
        //        tpFrm.Invoke((EventHandler)delegate
        //        {
        //            tpFrm.Close();
        //            tpFrm.Dispose();
        //        });
        //        return false;
        //    }
        //    tpFrm.Invoke((EventHandler)delegate
        //    {
        //        tpFrm.Close();
        //        tpFrm.Dispose();
        //    });
        //    return true;
        //}
        //public bool TPSlide_TestStart()  //w461c
        //{
        //    string sCmd = "AT^TPSLIDETESTSTART\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}

        //public bool Volt_Test(string station,out string info)  //w461c
        //{
        //    info = "";
        //    string sCmd = "";
        //    if (station == "QB001" || station == "QB002")
        //    {
        //         sCmd = "AT+EBMT=0\n";
        //    }
        //    else
        //    {
        //        sCmd = "AT^BMT=0\n";
        //    }
        //    Thread.Sleep(700);
        //    if (!SendStr(sCmd))
        //        return false;

        //    Thread.Sleep(700);
        //    info = ReadAtCommand();
        //    mainFrm.SendComMsg(info);
        //    if (!info.Contains("OK"))
        //        return false;
        //    return true;
        //}


        //public bool ICharger_Test(string station, out string info)  //w461c
        //{
        //    info = "";
        //    string sCmd = "";
        //    if (station == "QB001" || station == "QB002")
        //    {
        //        sCmd = "AT+EBMT=0\n";
        //    }
        //    else
        //    {
        //        sCmd = "AT^BMT=0\n";
        //    }
        //    if (!SendStr(sCmd))
        //        return false;

        //    Thread.Sleep(700);
        //    info = ReadAtCommand();
        //    mainFrm.SendComMsg(info);
        //    if (!info.Contains("OK"))
        //        return false;
        //    return true;
        //}


        //public bool TpFwUpdate_Test()  //w461c
        //{

        //    bool result = false;
        //    string sCmd = "AT+ETPFW\n";
        //    string infomation = "";
        //    for (int i = 0; i < 3; i++)
        //    {
        //        SendStr(sCmd);
        //        Thread.Sleep(3000);

        //        infomation = ReadAtCommand();
        //        if (infomation.Contains("TP firmware upgrade success"))
        //        {
        //            result = true;
        //            break;
        //        }
        //    }
        //    return result;
        //}
        //public bool  BleSignal(int limit,ref int rssi)
        //{
        //    int result = 999;
        //    bool final = false;
        //    string sCmd1= "AT^TALE=bton\n";
        //    string sCmd2 = "AT^BLERFTEST\n";
        //    string sCmd3 = "AT^TALE=btoff\n";
        //    bool canreadbt = false;
        //    //portComm.DiscardOutBuffer();
        //    //SendStr(sCmd1);
        //    //Thread.Sleep(1000);
        //    //ReadAtCommand();
        //    //SendStr(sCmd2);
        //    //Thread.Sleep(2000);
        //    string infomation = "";
        //    for (int i = 0; i < 6; i++)
        //    {
                

        //        SendStr(sCmd1);
        //        Thread.Sleep(2000);
        //        ReadAtCommand();
        //        SendStr(sCmd2);

        //        for (int j = 0; j < 80; j++)
        //        {
        //        Thread.Sleep(50);

        //            infomation = ReadAtCommand();
        //            //Thread.Sleep(1000);

        //            if (infomation.Contains("BLE RF TEST:"))
        //            {
        //                canreadbt = true;
        //                break;
                       
        //            }
        //        }
        //        if (canreadbt)
        //        {
        //            canreadbt = false;
        //            break;
        //        }
        //        Thread.Sleep(500);
        //    }
        // string[] detail = infomation.Split('\n', '\r', ':');
        // for (int i = 0; i < detail.Length; i++)
        // {
        //     if (detail[i].Contains("rssi"))
        //     {
        //         rssi = Convert.ToInt32(detail[i + 1]);
        //         if (rssi < limit)
        //         {
        //             final = true;
        //             break;
        //         }
 
        //     }
        // }
        // SendStr(sCmd3);
        // Thread.Sleep(500);
        // if (final)
        //     return true;
        // else
        // {
        //     rssi = 999;
        //     return false;
        // }
        //}
        //public bool Nandflash_Test()  //w461c
        //{

        //    bool result = false;
        //    string sCmd = "AT^TALE=nandtest\n";
        //    string infomation = "";

        //    SendStr(sCmd);
        //    Thread.Sleep(500);

        //    infomation = ReadAtCommand();
        //    if (infomation.Contains("NAND FLASH FS TEST OK"))
        //    {
        //        result = true;
                
        //    }

        //    return result;
        //}

        //public bool TpFwVersion_Test(out string info)  //w461c
        //{
        //    bool result = false;
        //    info = "";
        //    string sCmd = "AT+ETPVER\n";
            
        //    for (int i = 0; i < 3; i++)
        //    {
        //        if (!SendStr(sCmd))
        //            result = false;
        //        Thread.Sleep(150);
        //        info = ReadAtCommand();
        //        if (info.Contains("OK"))
        //        {
        //            result = true;
        //            break;
        //        }
        //    }
        //    return result;
        //}

        //public bool TPSlide_TestStop()  //w461c
        //{
        //    string sCmd = "AT^TPSLIDETESTSTOP\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}

        //public bool TPSlide_SlideTest(string ModelName, int inTry)
        //{
        //    int itrys = inTry;
        //    string rsMsg = "";
        //    bool TPSlideDown = false, TPSlideRight = false, TPSlideUp = false, TPSlideLeft = false;
        //    //if (ModelName.ToUpper() != "W461C")
        //    //{
        //    //    return false;
        //    //}
        //    FormTPSLIDE tpSlideFrm = null;
        //    new Thread((ThreadStart)delegate
        //    {
        //        tpSlideFrm = new FormTPSLIDE();
        //        Application.Run(tpSlideFrm);
        //    }).Start();
        //    while (tpSlideFrm == null)
        //        Thread.Sleep(100);

        //    while (itrys >= 0)
        //    {
        //        tpSlideFrm.Invoke((EventHandler)delegate
        //        {
        //            tpSlideFrm.label1.Text = "TP滑屏测试，请按指示方向开始滑动..." + itrys.ToString();
        //        });

        //        Thread.Sleep(1000);
        //        rsMsg = ReadAtCommand();
        //        if (rsMsg.Contains("tp slide test failed"))
        //        {
        //            tpSlideFrm.Invoke((EventHandler)delegate
        //            {
        //                tpSlideFrm.Close();
        //                tpSlideFrm.Dispose();
        //            });
        //            return false;
        //        }

        //        if (!TPSlideDown)
        //        {
        //            if (rsMsg.Contains("down slide"))
        //            {
        //                TPSlideDown = true;
        //                tpSlideFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpSlideFrm.bt_TPDown.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (!TPSlideRight)
        //        {
        //            if (rsMsg.Contains("right slide"))
        //            {
        //                TPSlideRight = true;
        //                tpSlideFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpSlideFrm.bt_TPRight.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (!TPSlideUp)
        //        {
        //            if (rsMsg.Contains("up slide"))
        //            {
        //                TPSlideUp = true;
        //                tpSlideFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpSlideFrm.bt_TPUp.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }
        //        if (!TPSlideLeft)
        //        {
        //            if (rsMsg.Contains("left slide"))
        //            {
        //                TPSlideLeft = true;
        //                tpSlideFrm.Invoke((EventHandler)delegate
        //                {
        //                    tpSlideFrm.bt_TPLeft.BackColor = System.Drawing.Color.Blue;
        //                });
        //            }
        //        }

        //        if (TPSlideDown && TPSlideRight && TPSlideUp && TPSlideLeft)
        //            break;

        //        itrys--;
        //    }
        //    if (itrys <= 0)
        //    {
        //        tpSlideFrm.Invoke((EventHandler)delegate
        //        {
        //            tpSlideFrm.Close();
        //            tpSlideFrm.Dispose();
        //        });
        //        return false;
        //    }

        //    tpSlideFrm.Invoke((EventHandler)delegate
        //    {
        //        tpSlideFrm.Close();
        //        tpSlideFrm.Dispose();
        //    });

        //    return true;
        //}
        //public bool MICTestON()
        //{
        //    string sCmd = "AT^MICTEST\n";
        //    portComm.DiscardOutBuffer();
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}

        //public bool MICTestProcess()
        //{
        //    string sCmd = "AT^MICTESTEND\n";
        //    string strRec = "";
        //    int nTrys = 10;
        //    int count = 0;

        //    do
        //    {
        //        nTrys--;
        //        strRec = ReadAtCommand();
        //        count = strRec.Length - strRec.Replace("MIC TEST OK", "IC TEST OK").Length;
        //        if (count >= 8)
        //        {
        //            break;
        //        }
        //        else
        //        {
        //            if (nTrys == 0)
        //            {
        //                SendStr(sCmd);
        //                return false;
        //            }

        //            Thread.Sleep(1000);
        //            continue;
        //        }
        //    } while (nTrys >= 0);

        //    if (!SendStr(sCmd))
        //        return false;

        //    return true;
        //}
        //public bool CameraTestON()
        //{
        //    string sCmd = "AT^CAMERATESTSTART\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("camera FM test mode"))
        //        return false;

        //    return true;
        //}
        //public bool CameraTestOFF()
        //{
        //    string sCmd = "AT^CAMERATESTSTOP\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("camera FM test mode"))
        //        return false;

        //    return true;
        //}
        //public bool KeyTestON(string ModelName)
        //{
        //    string sCmd = "";
        //    if (ModelName.ToUpper() == "W362" || ModelName.ToUpper() == "W461" || ModelName.ToUpper() == "W461C"
        //        || ModelName.ToUpper() == "4C" || ModelName.ToUpper() == "W602" || ModelName.ToUpper() == "QB001" || ModelName.ToUpper() == "QB002" || ModelName.ToUpper() == "QB003" || ModelName.ToUpper() == "QB004" || ModelName.ToUpper() == "QB005")
        //    {
        //        sCmd = "AT^KEYTESTBEGIN\n";
        //    }
        //    else
        //    {
        //        sCmd = "AT^KEYTEST\n";

        //    }
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    return true;
        //}
        //public bool KeyPressTest(string ModelName, int inTry)
        //{
        //    int itrys = inTry;
        //    string rsMsg = "";
        //    bool key1 = false, key2 = false;
        //    bool testResult = true;

        //    FormKeyTest keyFrm = null;
        //    new Thread((ThreadStart)delegate
        //    {
        //        keyFrm = new FormKeyTest();
        //        Application.Run(keyFrm);
        //    }).Start();
        //    while (keyFrm == null)
        //        Thread.Sleep(100);

        //    //if (ModelName.ToUpper() == "W607")
        //    //{
        //    //    portComm.DiscardInBuffer();
        //    //    Thread.Sleep(500);
        //    //}

        //    while (itrys >= 0)
        //    {
        //        keyFrm.Invoke((EventHandler)delegate { keyFrm.label1.Text = "按键测试，请按产品按键开始..." + itrys.ToString(); });

        //        Thread.Sleep(1000);
        //        rsMsg = ReadAtCommand();
        //        if (ModelName.ToUpper() == "W2" || ModelName.ToUpper() == "W361")
        //        {
        //            if (!key1)
        //            {
        //                if (rsMsg.Contains("KEY_1"))
        //                {
        //                    key1 = true;
        //                    keyFrm.Invoke((EventHandler)delegate { keyFrm.lab_PowerKey.ForeColor = System.Drawing.Color.Blue; });
        //                }
        //            }
        //            if (!key2)
        //            {
        //                if (rsMsg.Contains("KEY_2"))
        //                {
        //                    key2 = true;
        //                    keyFrm.Invoke((EventHandler)delegate { keyFrm.lab_SeclectKey.ForeColor = System.Drawing.Color.Blue; });
        //                }
        //            }
        //            if (key1 && key2)
        //                break;
        //        }
        //        else if (ModelName.ToUpper() == "W4" || ModelName.ToUpper() == "W4B")
        //        {
        //            if (rsMsg.Contains("KEY1") || rsMsg.Contains("KEY_1"))
        //            {
        //                keyFrm.Invoke((EventHandler)delegate { keyFrm.lab_PowerKey.ForeColor = System.Drawing.Color.Blue; });
        //                break;
        //            }
        //        }
        //        else if (ModelName.ToUpper() == "W362" || ModelName.ToUpper() == "W461"
        //            || ModelName.ToUpper() == "W461C")
        //        {
        //            if (!key1)
        //            {
        //                if (rsMsg.Contains("PowerKeyDown") && rsMsg.Contains("PowerKeyUp"))
        //                {
        //                    key1 = true;
        //                    keyFrm.Invoke((EventHandler)delegate { keyFrm.lab_PowerKey.ForeColor = System.Drawing.Color.Blue; });
        //                }
        //            }
        //            if (!key2)
        //            {
        //                if (rsMsg.Contains("SeclectKeyDown") && rsMsg.Contains("SeclectKeyUp"))
        //                {
        //                    key2 = true;
        //                    keyFrm.Invoke((EventHandler)delegate { keyFrm.lab_SeclectKey.ForeColor = System.Drawing.Color.Blue; });
        //                }
        //            }
        //            if (key1 && key2)
        //                break;
        //        }
        //        else if (ModelName.ToUpper() == "4C" || ModelName.ToUpper() == "QB001" || ModelName.ToUpper() == "QB002")
        //        {
        //            if (rsMsg.Contains("PowerDown") && rsMsg.Contains("PowerUp"))
        //            {
        //                keyFrm.Invoke((EventHandler)delegate { keyFrm.lab_PowerKey.ForeColor = System.Drawing.Color.Blue; });
        //                break;
        //            }
        //        }
        //        else if (ModelName.ToUpper() == "W602" || ModelName.ToUpper() == "QB003" || ModelName.ToUpper() == "QB004" || ModelName.ToUpper() == "QB005")
        //        {
        //            if (rsMsg.Contains("PowerKeyDown") && rsMsg.Contains("PowerKeyUp"))
        //            {
        //                keyFrm.Invoke((EventHandler)delegate { keyFrm.lab_PowerKey.ForeColor = System.Drawing.Color.Blue; });
        //                break;
        //            }
        //        }
        //        else
        //        {
        //            keyFrm.Invoke((EventHandler)delegate
        //            {
        //                keyFrm.Close();
        //                keyFrm.Dispose();
        //            });
        //            return false;
        //        }

        //        itrys--;
        //    }
        //    if (itrys <= 0)
        //    {
        //        testResult = false;
        //    }
        //    if (ModelName.ToUpper() == "W362" || ModelName.ToUpper() == "W461"
        //       || ModelName.ToUpper() == "W461C" || ModelName.ToUpper() == "4C" || ModelName.ToUpper() == "QB001" || ModelName.ToUpper() == "QB002" || ModelName.ToUpper() == "QB003" || ModelName.ToUpper() == "QB004" || ModelName.ToUpper() == "QB005" || ModelName.ToUpper() == "W602")
        //    {
        //        string sCmd = "AT^KEYTESTEND\n";
        //        SendStr(sCmd);
        //    }

        //    keyFrm.Invoke((EventHandler)delegate
        //    {
        //        keyFrm.Close();
        //        keyFrm.Dispose();
        //    });

        //    return testResult;
        //}

        //public bool VibratorON()
        //{
        //    string sCmd = "AT^VIBON\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}

        ////2.Andy New_Add_2
      
        //public bool ChargeIn()
        //{
        //    string sCmd = "AT^OVICHARGE\n";
        //    if (!SendStr(sCmd))
        //    {
        //        return false;
        //    }
        //    if (!ReadAtCommand().Contains("Charge_in"))
        //    {
        //        return false;
        //    }
        //    return true;
        //}

        //end add_2


        //public bool VibratorOff()
        //{
        //    string sCmd = "AT^VIBOFF\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}
        //public bool SpeakerON()
        //{
        //    string sCmd = "AT^SPKON\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}
        //public bool SpeakerOff()
        //{
        //    string sCmd = "AT^SPKOFF\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}

        //public bool G_SenserTest(string ModelName)
        //{
        //    string sCmd = "";

        //    if (ModelName.ToUpper() == "W362" || ModelName.ToUpper() == "W461"
        //        || ModelName.ToUpper() == "W461C")
        //    {
        //        try
        //        {
        //            SendStr("AT^ENBLEFM\n");
        //            sCmd = "AT^BLEGSENSOR\n";
        //            if (!SendStr(sCmd))
        //                return false;
        //            if (!ReadAtCommand().Contains("Gsensor OK"))
        //                return false;
        //        }
        //        catch (Exception)
        //        {
        //        }
        //        finally
        //        {
        //            SendStr("AT^DISBLEFM\n");
        //        }
        //    }
        //    else if (ModelName.ToUpper() == "4C")
        //    {
        //        try
        //        {
        //            //SendStr("AT^ENBLEFM\n");
        //            sCmd = "AT^BLEGSENSOR\n";
        //            if (!SendStr(sCmd))
        //                return false;
        //            if (!ReadAtCommand().Contains("Gsenso OK <1>"))
        //                return false;
        //        }
        //        catch (Exception)
        //        {
        //        }
        //        //finally
        //        //{
        //        //    SendStr("AT^DISBLEFM\n");
        //        //}
        //    }
        //    else if (ModelName.ToUpper() == "QB001")
        //    {
        //        try
        //        {
        //            //SendStr("AT^ENBLEFM\n");
        //            sCmd = "AT+BLEGSENSOR\n";
        //            if (!SendStr(sCmd))
        //                return false;
        //            if (!ReadAtCommandgsensor().Contains("Self-test PASS"))
        //                return false;
        //        }
        //        catch (Exception)
        //        {
        //        }
        //        //finally
        //        //{
        //        //    SendStr("AT^DISBLEFM\n");
        //        //}
        //    }
        //    else if (ModelName.ToUpper() == "W602")
        //    {
        //        try
        //        {
        //            SendStr("AT^ENBLEFM\n");
        //            sCmd = "AT^BLEGSENSOR\n";
        //            if (!SendStr(sCmd))
        //                return false;
        //            Thread.Sleep(500);
        //            if (!ReadAtCommand().Contains("Gsenso OK <1>"))
        //                return false;
        //        }
        //        catch (Exception)
        //        {
        //        }
        //        finally
        //        {
        //            SendStr("AT^DISBLEFM\n");
        //            Thread.Sleep(500);
        //        }
        //    }
        //    else if (ModelName.ToUpper() == "QB003" || ModelName.ToUpper() == "QB004" || ModelName.ToUpper() == "QB005")
        //    {
        //        try
        //        {
        //            //SendStr("AT^ENBLEFM\n");
        //            sCmd = "AT^BLEGSENSOR\n";
        //            if (!SendStr(sCmd))
        //                return false;
        //            Thread.Sleep(500);
        //            if (!ReadAtCommand().Contains("Gsenso OK <1>"))
        //                return false;
        //        }
        //        catch (Exception)
        //        {
        //        }
        //        //finally
        //        //{
        //        //    SendStr("AT^DISBLEFM\n");
        //        //    Thread.Sleep(500);
        //        //}
        //    }
        //    else
        //    {
        //        sCmd = "AT^PMTEST\n";
        //        if (!SendStr(sCmd))
        //            return false;
        //        if (!ReadAtCommand().Contains("STEP =1"))
        //            return false;

        //    }


        //    return true;
        //}

        //public bool EnbleBleFTM()
        //{
        //    string sCmd = "AT^ENBLEFM\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}
        //public bool DisBleFTM()
        //{
        //    string sCmd = "AT^DISBLEFM\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;

        //    return true;
        //}

        /// <summary> 
        /// 开启蓝牙测试
        /// </summary>  
        /// <param>
        /// out:bt_mac,输出BT MAC
        /// </param> 
        //public bool BTTest(ref string bt_mac)
        //{
        //    string strRec = "";
        //    bt_mac = "";
        //    string sCmd = "AT^BTTEST\n";
        //    try
        //    {
        //        if (!SendStr(sCmd))
        //            return false;
        //        strRec = ReadAtCommand();
        //        if (!strRec.Contains("BT_MAC="))
        //            return false;
        //        string[] strArray1 = { "BT_MAC=" };
        //        string[] strArray2 = { "\r\n" };
        //        bt_mac = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Replace(":", "").ToUpper();
        //        //if (bt_mac.Length < 10)
        //        //    return false;
        //    }
        //    catch (Exception)
        //    {

        //        bt_mac = "";
        //        return false;
        //    }

        //    return true;
        //}


        //for w362 ble mac load.
        //public bool GetBleMac(out string bt_mac)
        //{
        //    string strRec = "";
        //    bt_mac = "";
        //    string sCmd = "AT^BLEMAC\n";

        //    try
        //    {
        //        SendStr("AT^ENBLEFM\n");
        //        if (!SendStr(sCmd))
        //            return false;
        //        Thread.Sleep(1000);
        //        strRec = ReadAtCommand();
        //        if (!strRec.Contains("BTMac:"))
        //            return false;
        //        string[] strArray1 = { "BTMac:<" };
        //        string[] strArray2 = { ">" };
        //        bt_mac = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Replace(":", "").ToUpper();
        //        //if (bt_mac.Length < 10)
        //        //    return false;
        //    }
        //    catch (Exception)
        //    {
        //        bt_mac = "";
        //        return false;
        //    }
        //    finally
        //    {
        //        SendStr("AT^DISBLEFM\n");
        //    }

        //    return true;
        //}

        /// <summary> 
        /// 开启WIFI测试
        /// </summary>  
        /// <param>
        /// out:outWifi_mac,输出WIFI MAC
        /// </param> 
        //public bool GetWifiMac(out string outWifi_mac)
        //{
        //    outWifi_mac = "";
        //    string strRec = "";
        //    string sCmd = "AT^WIFITEST\n";
        //    try
        //    {
        //        if (!SendStr(sCmd))
        //            return false;
        //        Thread.Sleep(2000);
        //        strRec = ReadAtCommand();
        //        if (!strRec.Contains("OK"))
        //            return false;
        //        string[] strArray1 = { "WIFI_MAC=" };
        //        string[] strArray2 = { "\r\n" };
        //        outWifi_mac = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //        outWifi_mac = outWifi_mac.Replace(":", "").ToUpper();
        //        outWifi_mac = outWifi_mac.Replace("\"", "").ToUpper();

        //    }
        //    catch (Exception)
        //    {
        //        outWifi_mac = "";
        //        return false;
        //    }


        //    return true;
        //}
        /// <summary> 
        /// 开启WIFI测试
        /// </summary>  
        /// <param>
        /// out:outWifi_mac,输出WIFI MAC
        /// </param> 
        //public bool WifiTest(out string outWifi_mac, out int outWifi_Rssi, string inApName, short inTimes, bool bReadRssi)
        //{
        //    int i = 0;
        //    outWifi_mac = "";
        //    outWifi_Rssi = -999;
        //    string strRec = "";
        //    string sCmd = "AT^WIFITEST\n";
        //    try
        //    {
        //        Thread.Sleep(500);
        //        if (!SendStr(sCmd))
        //            return false;
        //        //Thread.Sleep(1000);
        //        //if (!SendStr(sCmd))
        //        //    return false;
                
        //        if (bReadRssi)
        //        {
        //            Thread.Sleep(inTimes);
        //        }

        //         string[] strArray4 = { "\r\n" };
        //        strRec = ReadAtCommand();
        //        if (!strRec.Contains("OK"))
        //            return false;
              
        //        string[] strArray1 = { "WIFI_MAC=" };
        //        string[] strArray = { "mac =" };
        //        string[] strArray2 = { "\r\n" };
              
        //        outWifi_mac = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //        outWifi_mac = outWifi_mac.Replace(":", "").ToUpper();
        //        outWifi_mac = outWifi_mac.Replace("\"", "").ToUpper();
        //        //string wifidetail=strRec.Replace(' ',',');
        //        string[] wifi=strRec.Split(' ');
        //        if (bReadRssi)
        //        {

        //            //int iWifiStatus = int.Parse(strRec.Substring(strRec.IndexOf("WIFI_STATUS=") + 12, 1).Trim());
        //            if (strRec.IndexOf("ssid") > 0)
        //            {
        //                foreach (string rssi in wifi)
        //                {
        //                    i++;
        //                    if (rssi.Contains(inApName))
        //                    {
        //                        outWifi_Rssi = Convert.ToInt32(wifi[i-4]);
        //                    }
        //                }
                        
        //            }
        //            else
        //            {
        //                outWifi_Rssi = -999;
        //                return false;
        //            }
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        outWifi_mac = "";
        //        outWifi_Rssi = -999;
        //        return false;
        //    }


        //    return true;
        //}

        //public bool Wifiw607Test(out string outWifi_mac, out int outWifi_Rssi, string inApName, short inTimes, bool bReadRssi)
        //{
        //    int i = 0;
        //    outWifi_mac = "";
        //    outWifi_Rssi = -999;
        //    string strRec = "";
        //    string sCmd = "AT^WIFITEST\n";
        //    try
        //    {
        //        if (!SendStr(sCmd))
        //            return false;
        //        if (bReadRssi)
        //        {
        //            Thread.Sleep(inTimes);
        //        }


        //        strRec = ReadAtCommand();
        //        if (!strRec.Contains("OK"))
        //            return false;
        //        //string[] strArray1 = { "WIFI_MAC=" };
        //        //string[] strArray2 = { "\r\n" };
        //        //outWifi_mac = strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0];
        //        //outWifi_mac = outWifi_mac.Replace(":", "").ToUpper();
        //        //outWifi_mac = outWifi_mac.Replace("\"", "").ToUpper();
        //        //string wifidetail=strRec.Replace(' ',',');
        //        string[] wifi = strRec.Split(' ');
        //        if (bReadRssi)
        //        {

        //            //int iWifiStatus = int.Parse(strRec.Substring(strRec.IndexOf("WIFI_STATUS=") + 12, 1).Trim());
        //            if (strRec.IndexOf("ssid") > 0)
        //            {
        //                foreach (string rssi in wifi)
        //                {
        //                    i++;
        //                    if (rssi.Contains(inApName))
        //                    {
        //                        outWifi_Rssi = Convert.ToInt32(wifi[i - 4]);
        //                    }
        //                }

        //            }
        //            else
        //            {
        //                outWifi_Rssi = -999;
        //                return false;
        //            }
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        outWifi_mac = "";
        //        outWifi_Rssi = -999;
        //        return false;
        //    }


        //    return true;
        //}
        //public bool WifiConnectTest(string inApName)
        //{
        //    string strRec = "";

        //    try
        //    {

        //        if (!SendStr(string.Format("AT^LEXINTEST={0},;\n", inApName)))
        //            return false;
        //        Thread.Sleep(4000);
        //        strRec = ReadAtCommand();
        //        if (strRec.Contains("WIFI CONNECTED OK") || strRec.Contains("WIFI GOT IP"))
        //            return true;
        //        else
        //            return false;
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

            
        //}
  
        //public bool GPS_ON()
        //{
        //    string sCmd = "AT^GPSON\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    bGpsOn = true;
        //    return true;
        //}
        //public bool GPS_OFF()
        //{
        //    string sCmd = "AT^GPSOFF\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    //if (!ReadAtCommand().Contains("OK"))
        //    //    return false;

        //    bGpsOn = false;
        //    return true;
        //}

        //public bool ATH_ON()
        //{
        //    string sCmd = "ATH;\n";
        //    if (!SendStr(sCmd))
        //        return false;
        //    Thread.Sleep(500);
        //    if (!ReadAtCommand().Contains("OK"))
        //        return false;
        //    return true;
        //}

        //public bool GPS_Positioning()
        //{
        //    string strRec = "";
        //    strRec = ReadAtCommand();
        //    if (!strRec.Contains("$gps_info=A")
        //         && !strRec.Contains("$gps_info = A"))
        //        return false;

        //    return true;
        //}
        //public bool GPS_Positioning(string sModel, int nCnValueSepc, out int iCNValue)
        //{
        //    string strRec = "";
        //    iCNValue = 0;

        //    strRec = ReadAtCommand();
        //    if (!strRec.Contains("$gps_info=A") && !strRec.Contains("$gps_info = A"))
        //    {
        //        return false;
        //    }
        //    else
        //    {
        //        if (sModel == "W362" || sModel == "W461" || sModel == "W461C" || sModel == "4C" || sModel == "W602" || sModel == "QB001" || sModel == "QB002" || sModel == "QB003" || sModel == "QB004" || sModel == "QB005")
        //        {
        //            string[] strArray1 = { "$gps_info=A," };
        //            string[] strArray2 = { "\r\n" };
        //            iCNValue = int.Parse(strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Trim());
        //            if (iCNValue >= nCnValueSepc)
        //            {
        //                return true;
        //            }
        //            else
        //            {
        //                return false;
        //            }
        //        }
        //    }

        //    return true;
        //}

        //public bool GPS_Positioning(string sModel, int nCnValueSepc, int nCnValueSepcMax, out int iCNValue)
        //{
            //string strRec = "";
            //iCNValue = 0;

            //strRec = ReadAtCommand();
            //if (!strRec.Contains("$gps_info=A") && !strRec.Contains("$gps_info = A"))
            //{
            //    return false;
            //}
            //else
            //{
            //    if (sModel == "W362" || sModel == "W461" || sModel == "W461C" || sModel == "4C" || sModel == "W602" || sModel == "W607")
            //    {
            //        string[] strArray1 = { "$gps_info=A," };
            //        string[] strArray2 = { "\r\n" };
            //        iCNValue = int.Parse(strRec.Split(strArray1, StringSplitOptions.None)[1].Split(strArray2, StringSplitOptions.None)[0].Trim());
            //        if (iCNValue >= nCnValueSepc && iCNValue <= nCnValueSepcMax)
            //        {
            //            return true;
            //        }
            //        else
            //        {
            //            return false;
            //        }
            //    }
            //}

            //return true;
        //}

        //public bool GPS_CNValueCheck(int specValue, out int iCNValue)
        //{
        //    string[] delimiterString = { "$gps_info=SNR1:", "\r\n" };
        //    char[] delimiterChars = { ',', ':', ',', ':' };
        //    string strRec = "";
        //    iCNValue = 0;

        //    try
        //    {
        //        strRec = ReadGPSInfo();
        //        if (!strRec.Contains("$gps_info=SNR1"))
        //            return false;

        //        string[] words = strRec.Split(delimiterString, StringSplitOptions.RemoveEmptyEntries);
        //        strRec = words[1];
        //        words = strRec.Split(delimiterChars);
        //        iCNValue = Convert.ToInt32(words[0].Trim());
        //        if (Convert.ToInt32(words[0].Trim()) < specValue
        //            && Convert.ToInt32(words[2].Trim()) < specValue
        //            && Convert.ToInt32(words[4].Trim()) < specValue)
        //        {
        //            return false;
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

        //    return true;
        //}

        //public bool GPS_CNValueCheck(int specValue,int specValuemax, out int iCNValue)
        //{
        //    string[] delimiterString = { "$gps_info=SNR1:", "\r\n" };
        //    char[] delimiterChars = { ',', ':', ',', ':' };
        //    string strRec = "";
        //    iCNValue = 0;

        //    try
        //    {
        //        strRec = ReadGPSInfo();
        //        if (!strRec.Contains("$gps_info=SNR1"))
        //            return false;

        //        string[] words = strRec.Split(delimiterString, StringSplitOptions.RemoveEmptyEntries);
        //        strRec = words[1];
        //        words = strRec.Split(delimiterChars);
        //        iCNValue = Convert.ToInt32(words[0].Trim());
        //        if (Convert.ToInt32(words[0].Trim()) < specValue
        //            && Convert.ToInt32(words[2].Trim()) < specValue
        //            && Convert.ToInt32(words[4].Trim()) < specValue && Convert.ToInt32(words[0].Trim()) > specValuemax && Convert.ToInt32(words[2].Trim()) > specValuemax && Convert.ToInt32(words[4].Trim()) > specValuemax)
        //        {
        //            return false;
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }

        //    return true;
        //}


        //public bool AT_CSQ(out int rxLev, out int rxQuallity)
        //{
        //    string strRec = "";
        //    string sCmd = "AT+CSQ\n";
        //    rxLev = 999;
        //    rxQuallity = 999;
        //    try
        //    {
              
        //        if (!SendStr(sCmd))
        //            return false;

        //        Thread.Sleep(2000);

        //        strRec = ReadAtCommand();
        //        if (!strRec.Contains("OK"))
        //            return false;
        //        if (strRec.IndexOf(":") > 0)
        //        {
        //            rxLev = int.Parse(strRec.Substring(strRec.IndexOf(":") + 1, 3).Trim());
        //            rxQuallity = int.Parse(strRec.Substring(strRec.IndexOf(",") + 1, 3).Trim());
        //        }
        //        else
        //        {
        //            rxLev = 999;
        //            rxQuallity = 999;
        //            return false;
        //        }
        //    }
        //    catch (Exception)
        //    {

        //        rxLev = 999;
        //        rxQuallity = 999;
        //        return false;
        //    }

        //    return true;
        //}
        //public bool ATD112()
        //{
        //    string sCmd = "ATD112;\n";
        //    try
        //    {
        //        if (!SendStr(sCmd))
        //            return false;
        //        Thread.Sleep(500);
        //        if (!ReadAtCommand().Contains("OK"))
        //            return false;
        //    }
        //    catch (Exception)
        //    {

        //        return false;
        //    }


        //    return true;
        //}

        //public bool ATE(bool bEcho)
        //{
        //    string sCmd;
        //    if (bEcho)
        //    {
        //        sCmd = "ATE 1\n";
        //    }
        //    else
        //    {
        //        sCmd = "ATE 0\n";
        //    }
        //    try
        //    {
        //        if (!SendStr(sCmd))
        //            return false;
        //        if (!ReadAtCommand().Contains("OK"))
        //            return false;
        //    }
        //    catch (Exception)
        //    {

        //        return false;
        //    }

        //    return true;
        //}

        //public bool SleepMode(bool bMode)
        //{
        //    string sCmd;
        //    if (bMode)
        //    {
        //        sCmd = "AT+ESLP=1\n";
        //    }
        //    else
        //    {
        //        sCmd = "AT+ESLP=0\n";
        //    }
        //    try
        //    {
        //        if (!SendStr(sCmd))
        //            return false;
        //        if (!ReadAtCommand().Contains("OK"))
        //            return false;
        //    }
        //    catch (Exception)
        //    {

        //        return false;
        //    }

        //    return true;
        //}
    }
    class WEBSERVICE
    {
        static string api_secret = "abcdefg";       // time 是 123456789，api_token 即是  md5('123456789abcdef') external
        string sEpoch = "";

        //海外版URL
        const string START_BIND_URL_EXT = "http://dev.misafes2.qiwocloud2.com/?action=bind&api_token=%API_TOKEN&qrcode=%QRCODE&sim=%SIMCODE&time=%TIME";

        //国内版URL
        const string START_BIND_URL_INT = "http://111.206.81.229/start_test?qr=%QRCODE&pn=%SIMCODE";
        // const string CHECK_BIND_URL_INT = "http://111.206.81.229/test_check?hc=%HARDCODE&imei=%IMEICODE&qr=%QRCODE";
        const string CHECK_BIND_URL_INT = "http://111.206.81.229/test_imei?imei=%IMEICODE";

        const string SIMULATIVE_START_URL = "http://m.baby.360.cn/factory/test?mode=1&pn=%SIMCODE&qr=%QRCODE";

        public WEBSERVICE()
        {

        }
        /// <summary> 
        /// 获取时间戳  
        /// </summary>  
        /// <returns></returns>  
        private string GetTimeStamp()
        {
            TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalSeconds).ToString();
        }
        /// <summary> 
        /// 获取api token  
        /// </summary>  
        /// <returns></returns> 
        private string GetApi_token()
        {
            sEpoch = GetTimeStamp();
            string api_token_BeforeMD5 = sEpoch + api_secret;
            MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();

            byte[] bytValue, bytHash;
            bytValue = Encoding.Default.GetBytes(api_token_BeforeMD5);
            bytHash = md5.ComputeHash(bytValue);
            md5.Clear();

            string sTemp = "";
            for (int i = 0; i < bytHash.Length; i++)
            {
                sTemp += bytHash[i].ToString("X").PadLeft(2, '0');
            }

            return sTemp.ToLower();
        }
        /// <summary> 
        /// 海外版绑定
        /// </summary>  
        /// <returns>
        /// TRUE:绑定成功
        /// FALSE:失败
        /// </returns> 
        public bool QRBind_EXT(string strQR, string strSIM, out int outErrCode, out string outErrMessage)
        {
            outErrCode = 0;
            outErrMessage = "";

            if ((strQR.Length <= 0) || (strSIM.Length <= 0))
            {
                outErrMessage = "参数错误";
                outErrCode = 400;
                return false;
            }

            string sURL = START_BIND_URL_EXT;
            sURL = sURL.Replace("%API_TOKEN", GetApi_token());
            sURL = sURL.Replace("%QRCODE", strQR);
            sURL = sURL.Replace("%SIMCODE", strSIM);
            sURL = sURL.Replace("%TIME", sEpoch);
            //创建一个HTTP请求
            try
            {
                HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(sURL);
                HttpWebResponse webreponse = (HttpWebResponse)webrequest.GetResponse();
                using (Stream stream = webreponse.GetResponseStream())
                {
                    byte[] rsByte = new Byte[webreponse.ContentLength];  //save data in the stream

                    stream.Read(rsByte, 0, (int)webreponse.ContentLength);
                    string strRs = System.Text.Encoding.UTF8.GetString(rsByte, 0, rsByte.Length).ToString();
                    stream.Close();

                    strRs = strRs.Replace("\"", "");
                    if (strRs.Contains(":0"))
                    {
                        outErrCode = 0;
                        outErrMessage = "绑定成功";
                    }
                    else if (strRs.Contains(":7110"))
                    {
                        outErrCode = 7110;
                        outErrMessage = "绑定错误";
                        return false;
                    }
                    else if (strRs.Contains(":1221"))
                    {
                        outErrCode = 1221;
                        outErrMessage = "sim 卡号非法";
                        return false;
                    }
                    else if (strRs.Contains(":1211"))
                    {
                        outErrCode = 1211;
                        outErrMessage = "QR code非法";
                        return false;
                    }
                    else
                    {
                        outErrCode = 7110;
                        outErrMessage = "绑定错误";
                        return false;
                    }
                }
            }
            catch (Exception exp)
            {
                outErrMessage = exp.ToString();
                outErrCode = 400;
                return false;
            }

            return true;
        }

        /// <summary> 
        /// 国内版绑定
        /// </summary>  
        /// <returns>
        /// TRUE:绑定成功
        /// FALSE:失败
        /// </returns> 
        public bool QRBind_INT(string strQR, string strSIM, out int outErrCode, out string outErrMessage)
        {
            outErrCode = 0;
            outErrMessage = "";

            if ((strQR.Length <= 0) || (strSIM.Length <= 0))
            {
                outErrMessage = "无效QR码或无效手机号";
                outErrCode = 1;
                return false;
            }

            string sURL = START_BIND_URL_INT;
            sURL = sURL.Replace("%QRCODE", strQR);
            sURL = sURL.Replace("%SIMCODE", strSIM);
            try
            {

                //创建一个HTTP请求
                HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(sURL);
                HttpWebResponse webreponse = (HttpWebResponse)webrequest.GetResponse();
                using (Stream stream = webreponse.GetResponseStream())
                {
                    byte[] rsByte = new Byte[webreponse.ContentLength];  //save data in the stream

                    stream.Read(rsByte, 0, (int)webreponse.ContentLength);
                    string strRs = System.Text.Encoding.UTF8.GetString(rsByte, 0, rsByte.Length).ToString();
                    stream.Close();
                    if (strRs.Contains("0"))
                    {
                        outErrCode = 0;
                        outErrMessage = "短信已下发";
                    }
                    else if (strRs.Contains("1"))
                    {
                        outErrCode = 1;
                        outErrMessage = "无效QR码或无效手机号";
                        return false;
                    }
                    else if (strRs.Contains("2"))
                    {
                        outErrCode = 2;
                        outErrMessage = "短信下发错误";
                        return false;
                    }
                    else if (strRs.Contains("3"))
                    {
                        outErrCode = 3;
                        outErrMessage = "错误的手机号或iccid";
                        return false;
                    }
                    else
                    {
                        outErrCode = 2;
                        outErrMessage = "短信下发错误";
                        return false;
                    }
                }
            }
            catch (Exception exp)
            {
                outErrMessage = exp.ToString();
                outErrCode = 2;
                return false;
            }

            return true;
        }

        /// <summary> 
        /// 国内版绑定Check
        /// </summary>  
        /// <returns>
        /// 返回已绑定的SIMCODE,返回0，没绑定
        /// </returns> 
        public string QRBind_Check_INT(string strIMEI)
        {

            if ((strIMEI.Length <= 0))
            {
                return "0";
            }

            string sURL = CHECK_BIND_URL_INT;
            sURL = sURL.Replace("%IMEICODE", strIMEI);
            try
            {
                //创建一个HTTP请求
                HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(sURL);
                HttpWebResponse webreponse = (HttpWebResponse)webrequest.GetResponse();
                using (Stream stream = webreponse.GetResponseStream())
                {
                    byte[] rsByte = new Byte[webreponse.ContentLength];  //save data in the stream

                    stream.Read(rsByte, 0, (int)webreponse.ContentLength);
                    string strRs = System.Text.Encoding.UTF8.GetString(rsByte, 0, rsByte.Length).ToString();
                    stream.Close();
                    return strRs;
                }

                //WebClient client = new WebClient();
                //using (Stream stream = client.OpenRead(sURL))
                //{
                //    byte[] rsByte = new Byte[stream.Length];  //save data in the stream

                //    stream.Read(rsByte, 0, (int)stream.Length);
                //    string strRs = System.Text.Encoding.UTF8.GetString(rsByte, 0, rsByte.Length).ToString();
                //    stream.Close();
                //    return strRs;
                //}
            }
            catch (Exception)
            {
                return "0";
            }
        }

        /// <summary> 
        /// 测试模拟国内版绑定
        /// </summary>  
        /// <returns>
        /// TRUE:绑定成功
        /// FALSE:失败
        /// </returns> 
        public bool Simulation_QRBind(string strQR, string strSIM, out int outErrCode, out string outErrMessage)
        {
            outErrCode = 0;
            outErrMessage = "";

            if ((strQR.Length <= 0) || (strSIM.Length <= 0))
            {
                outErrMessage = "无效QR码或无效手机号";
                outErrCode = 1;
                return false;
            }

            string sURL = SIMULATIVE_START_URL;
            sURL = sURL.Replace("%QRCODE", strQR);
            sURL = sURL.Replace("%SIMCODE", strSIM);
            try
            {

                //创建一个HTTP请求
                HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(sURL);
                HttpWebResponse webreponse = (HttpWebResponse)webrequest.GetResponse();
                using (Stream stream = webreponse.GetResponseStream())
                {
                    byte[] rsByte = new Byte[webreponse.ContentLength];  //save data in the stream

                    stream.Read(rsByte, 0, (int)webreponse.ContentLength);
                    string strRs = System.Text.Encoding.UTF8.GetString(rsByte, 0, rsByte.Length).ToString();
                    stream.Close();
                    if (strRs.Contains("0"))
                    {
                        outErrCode = 0;
                        outErrMessage = "短信已下发";
                    }
                    else if (strRs.Contains("1"))
                    {
                        outErrCode = 1;
                        outErrMessage = "无效QR码或无效手机号";
                        return false;
                    }
                    else if (strRs.Contains("2"))
                    {
                        outErrCode = 2;
                        outErrMessage = "短信下发错误";
                        return false;
                    }
                    else if (strRs.Contains("3"))
                    {
                        outErrCode = 3;
                        outErrMessage = "错误的手机号或iccid";
                        return false;
                    }
                    else
                    {
                        outErrCode = 2;
                        outErrMessage = "短信下发错误";
                        return false;
                    }
                }
            }
            catch (Exception exp)
            {
                outErrMessage = exp.ToString();
                outErrCode = 2;
                return false;
            }

            return true;
        }
    }
}