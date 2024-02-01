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
        public List<DevicePort> lSpecialComPorts;
        string sOutMsg;
        public static bool bDataReceived;
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

        

        public COMPORT()  //结构函数
        {
            nPortNo = 1;
            portComm = new SerialPort();
            portComm.BaudRate = 115200;
            portComm.PortName = "COM1";
            portComm.ReadBufferSize = 1024 * 1024;
            portComm.WriteBufferSize = 1024 * 1024;
            portComm.NewLine = "\r\n";
            

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
                COMPORT.bDataReceived = true;
                return;
            }
            byte OperationResult = ReadBuffer[2];
            if (0x00 != OperationResult)
            {
                if (0x02 == OperationResult)//芯片连接异常
                {
                    UpdateLogRecord("请检查连接");
                    IQT.mainForm.ReTest = true;
                }
                else
                    IQT.mainForm.ReTest = false;
                IQT.mainForm.TestDone = true;
                COMPORT.bDataReceived = true;
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
                COMPORT.bDataReceived = true;
            }
            COMPORT.bDataReceived = true;
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
                                TestInfo.ROMExceptionFirstTime = DateTime.Now;
                                //result.ROM.FirstTime = DateTime.Now;
                            }
                            if (0 != (exceptionValue & (1 << (byte)ExceptionBitMask.DROM_READ % 8)))
                            {
                                result.ROM.Exception = true;
                                if (!result.ROMErrDict.ContainsKey("DROM"))
                                    result.ROMErrDict.Add("DROM", DateTime.Now);
                                TestInfo.ROMExceptionFirstTime = DateTime.Now;
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
            public static DateTime ROMExceptionFirstTime
            {
                get { return promExceptionFirstTime; }
                set
                {
                    //if (value < ROMExceptionFirstTime& value!= DateTime.MinValue)
                    { 
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
                    //if (value < DROMExceptionFirstTime & value != DateTime.MinValue)
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
                    //if (value < TRIMExceptionFirstTime & value != DateTime.MinValue)
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
                    //if (value < IPExceptionFirstTime & value != DateTime.MinValue)
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
            public static bool OverallStatus;
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

        public bool Open()
        {
            if (!portComm.IsOpen)
            {
                try
                {
                    portComm.Open();
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return true;
        }

        public void Close()
        {
            if (portComm.IsOpen)
            {
                try
                {
                    portComm.DiscardInBuffer();
                    portComm.DiscardOutBuffer();
                    portComm.Close();
                }
                catch (Exception)
                {
                    ;
                }
                finally
                {
                    portComm.Dispose();
                }
            }

        }



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

            //PortName = lSpecialComPorts[0].PortName;
           
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
    
     

      
    }
}