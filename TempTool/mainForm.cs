using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.PropertyGridInternal;
using CommPort;
using static CommPort.COMPORT;

namespace IQT
{
    public partial class mainForm : Form
    {
        COMPORT port;//串口实例
        DataGridView gridView;//数据表格的宏定义,方便添加

        public static UInt32 startNumber = 0;
        static UInt32 endNumber = 0;

        static UInt32 currentNumber = 0;
        public static Queue<UInt32> boardNumberQueue;
        public static object SRFlag=new object();

        private static int IntervalHour = 0;
        private static int IntervalMinute = 1;
        private static DateTime lastTestTime = DateTime.MinValue;

        //是否需要重测
        public static bool ReTest = false;
        //重测次数
        public static int ReTestCount = 3;
        //测试结束标志
        public static bool TestDone = false;
        //异常类型
        public static string ExceptionType = string.Empty;
        //异常信息
        public static string ExceptionString =string.Empty;
        //测试结果
        
        //EFLASH异常
        private static DateTime eFLASHFirstExceptionTime = DateTime.MaxValue;
        public static DateTime EFLASHFirstExceptionTime
        {
            get { return eFLASHFirstExceptionTime; }
            set
            {
                if (value < EFLASHFirstExceptionTime & value != DateTime.MinValue)
                {
                    eFLASHFirstExceptionTime = value;
                }
            }
        }

        //TRIM异常
        private static DateTime trimFirstExceptionTime = DateTime.MaxValue;
        public static DateTime TrimFirstExceptionTime
        {
            get { return trimFirstExceptionTime; }
            set
            {
                if (value < TrimFirstExceptionTime & value != DateTime.MinValue)
                {
                    trimFirstExceptionTime = value;
                }
            }
        }
        object lockObj = new object();


        Thread autoTestThread;
        public mainForm()
        {
            InitializeComponent();
        }
        private void mainForm_Load(object sender, EventArgs e)
        {
            port = new COMPORT();
            port.UpdateStatus += UpdateTestStatus;
            port.UpdatePortRecord += UpdatePortRecord;
            port.UpdateLogRecord += UpdateLogRecord;
            if (port.ScanComPort(2, 1, 1))
            {
                SerialPortCbx.Items.Add( port.PortName );
            }
            if(0 != SerialPortCbx.Items.Count)
            {
                SerialPortCbx.SelectedIndex = 0;
                port.PortName = SerialPortCbx.Text;
                if(!port.Open())
                    MessageBox.Show("串口打开失败,可能被占用,请检查");
            }
            InitGDV();
            Number_ValueChanged(null, null);
            gridView.CellPainting += GridView_CellPainting;
            var clearMenuItem = new MenuItem("清空");
            clearMenuItem.Click += (Sender, EventArgs) => PortRecordRtbx.Clear();
            var contextMenu = new ContextMenu();
            contextMenu.MenuItems.Add(clearMenuItem);  
            PortRecordRtbx.ContextMenu = contextMenu;

            var clearMenuItem1 = new MenuItem("清空");
            clearMenuItem1.Click += (Sender, EventArgs) => ErrorLogRtbx.Clear();
            var contextMenu1 = new ContextMenu();
            contextMenu1.MenuItems.Add(clearMenuItem1);  
            ErrorLogRtbx.ContextMenu = contextMenu1;

            StartNumber.Value = TempTool.Properties.Settings.Default.start;
            EndNumber.Value = TempTool.Properties.Settings.Default.end;

            ReadAddressTbx.Text = TempTool.Properties.Settings.Default.readAddress;
            WriteAddressTbx.Text = TempTool.Properties.Settings.Default.writeAddress;

            autoTestThread = new Thread(() =>
            {
                while (true)
                {
                    var nextDateTime = lastTestTime.AddHours(IntervalHour).AddMinutes(IntervalMinute);
                    if (DateTime.Now.Hour == nextDateTime.Hour & DateTime.Now.Minute == nextDateTime.Minute)
                    {
                        testFlag = true;
                    }
                    Thread.Sleep(200);

                    if(testFlag)
                        StartTest(null, null);
                }
            }
            );
            autoTestThread.IsBackground = true;
            autoTestThread.Start();
        }

        private void GridView_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            //string flag = TabCtrl.SelectedTab.ToolTipText;
            DataGridView dgv = (DataGridView)sender;
            // 对相同单元格进行合并
            // 3 BitName列, 4 BitValue列 //|| 4 == e.ColumnIndex || 5 == e.ColumnIndex
            if ((0 == e.ColumnIndex) && e.RowIndex != -1)
            {
                using
                    (
                    Brush gridBrush = new SolidBrush(dgv.GridColor),
                    backColorBrush = new SolidBrush(e.CellStyle.BackColor)
                    )
                {
                    using (Pen gridLinePen = new Pen(gridBrush))
                    {
                        // 清除单元格
                        e.Graphics.FillRectangle(backColorBrush, e.CellBounds);
                        // 画 Grid 边线（仅画单元格的底边线和右边线）
                        //   如果下一行和当前行的数据不同，则在当前的单元格画一条底边线
                        if (e.RowIndex < dgv.Rows.Count - 2 &&
                        dgv.Rows[e.RowIndex + 1].Cells[e.ColumnIndex].Value.ToString() !=
                        e.Value.ToString())
                        {
                            e.Graphics.DrawLine(gridLinePen, e.CellBounds.Left,
                            e.CellBounds.Bottom - 1, e.CellBounds.Right - 1,
                            e.CellBounds.Bottom - 1);
                        }
                        // 画右边线
                        e.Graphics.DrawLine(gridLinePen, e.CellBounds.Right - 1,
                            e.CellBounds.Top, e.CellBounds.Right - 1,
                            e.CellBounds.Bottom);

                        StringFormat sf = new StringFormat();
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;

                        // 画（填写）单元格内容，相同的内容的单元格只填写第一个
                        if (e.Value != null)
                        {
                            if (e.RowIndex > 0 &&
                            dgv.Rows[e.RowIndex - 1].Cells[e.ColumnIndex].Value.ToString() ==
                            e.Value.ToString()) { }
                            else
                            {
                                e.Graphics.DrawString(e.Value.ToString(), e.CellStyle.Font,
                                    Brushes.Black, e.CellBounds.X + 2,
                                    e.CellBounds.Y + 5, StringFormat.GenericTypographic);
                            }
                        }
                        e.Handled = true;//这一句非常重要，必须加上，要不所画的内容就被后面的Painting事件刷新不见了！！！
                    }
                }
            }
        }

        bool testFlag=false;
        static DataGridViewColumn IDCol;
        static DataGridViewColumn ResultCol;
        static DataGridViewColumn ErrorItemCol;
        static DataGridViewColumn ErrorTypeCol;
        static DataGridViewColumn FirstErrorTimeCol;
        static DataGridViewColumn StatusCol;
        void InitGDV()
        {
            gridView = new DataGridView();
            gridView.Dock = DockStyle.Fill;
            gridView.Visible = true;
            gridView.AutoGenerateColumns = true;//不自动生成列,自定义,不显示id列
            gridView.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;//表头居中
            gridView.RowsDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;//单元格内容居中 
            gridView.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            gridView.EnableHeadersVisualStyles = false;//控制是否使用用户自定义主题表头
            gridView.RowHeadersVisible = true;
            //gridView.RowHeadersVisible = false;
            gridView.ColumnHeadersDefaultCellStyle.BackColor = Color.LightGray;
            gridView.DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
            //gridView.SelectionMode = DataGridViewSelectionMode.ColumnHeaderSelect;

            IDCol = new DataGridViewColumn();
            IDCol.DataPropertyName = "ChipID";
            IDCol.HeaderText = "板号";
            IDCol.ReadOnly = true;
            IDCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            IDCol.Width = 40;
            IDCol.Visible = true;

            ErrorItemCol = new DataGridViewColumn();
            ErrorItemCol.DataPropertyName = "ErrorItem";
            ErrorItemCol.HeaderText = "测试项目";
            ErrorItemCol.ReadOnly = true;
            ErrorItemCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            ErrorItemCol.Visible = true;
            ErrorItemCol.Width = 80;
            ErrorItemCol.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

            ErrorTypeCol = new DataGridViewColumn();
            ErrorTypeCol.DataPropertyName = "ErrorType";
            ErrorTypeCol.HeaderText = "异常类型";
            ErrorTypeCol.ReadOnly = true;
            ErrorTypeCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            ErrorTypeCol.Visible = true;
            ErrorTypeCol.DefaultCellStyle.WrapMode = DataGridViewTriState.True; 
            
            StatusCol = new DataGridViewColumn();
            StatusCol.DataPropertyName = "TimeStamp";
            StatusCol.HeaderText = "当前状态";
            StatusCol.ReadOnly = true;
            //StatusCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            StatusCol.SortMode = DataGridViewColumnSortMode.Automatic;
            StatusCol.Visible = true;
            StatusCol.DefaultCellStyle.WrapMode = DataGridViewTriState.True; 

            FirstErrorTimeCol = new DataGridViewColumn();
            FirstErrorTimeCol.DataPropertyName = "FirstTimeStamp";
            FirstErrorTimeCol.HeaderText = "首次异常时间";
            FirstErrorTimeCol.ReadOnly = true;
            FirstErrorTimeCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            FirstErrorTimeCol.Visible = true;
            FirstErrorTimeCol.Width = 160;
            FirstErrorTimeCol.DefaultCellStyle.WrapMode = DataGridViewTriState.True; 

            ResultCol = new DataGridViewColumn();
            ResultCol.DataPropertyName = "Result";
            ResultCol.HeaderText = "测试结果";
            ResultCol.ReadOnly = true;
            ResultCol.SortMode = DataGridViewColumnSortMode.NotSortable;
            ResultCol.Visible = true;
           

            DataGridViewCell dgvcell = new DataGridViewTextBoxCell();
            IDCol.CellTemplate = dgvcell;
            ResultCol.CellTemplate = dgvcell;
            ErrorItemCol.CellTemplate = dgvcell;
            ErrorTypeCol.CellTemplate = dgvcell;
            FirstErrorTimeCol.CellTemplate = dgvcell;
            StatusCol.CellTemplate = dgvcell;

            gridView.Columns.Add(IDCol);
            gridView.Columns.Add(ErrorItemCol);
            gridView.Columns.Add(ErrorTypeCol);
            gridView.Columns.Add(FirstErrorTimeCol);
            gridView.Columns.Add(StatusCol);
            gridView.Columns.Add(ResultCol);

            SchePage.Controls.Clear();
            SchePage.Controls.Add(gridView);
        }
      
        void OpenPort(uint portNumber)
        {
            string errMsg = string.Empty;
            byte[] cmd;
            cmd = PackCmd(OperaType.REPOST,  PackBoardControlCmd(0x68, (byte)portNumber));
            port.SendData(cmd,ref errMsg);
            currentNumber = portNumber;
        }
        void ClosePort(uint portNumber)
        {
            string errMsg = string.Empty;
            byte[] cmd;
            cmd = PackCmd(OperaType.REPOST,  PackBoardControlCmd(0x69, (byte)portNumber));
            port.SendData(cmd,ref errMsg);
        }

        private static byte[] ReadAddress = new byte[4] {  0x20, 0x00, 0x02, 0x00 };
        private static byte[] WriteAddress = new byte[4] {  0x20, 0x00, 0x02, 0x00 };
        void SendReadCMD()
        {
            TestDone = false;
            string errMsg = string.Empty; 
            byte[] cmd;
            //byte[] address = new byte[4] { 0x20, 0x00, 0x02, 0x00 };
            //byte[] address = new byte[4] { 0x00, 0x00, 0x00, 0x00 };
            cmd = PackCmd(OperaType.READ, ReadAddress);
            port.SendData(cmd,ref errMsg);
        }
        void SendWriteCMD()
        {
            TestDone = false;
            string errMsg = string.Empty; 
            byte[] cmd;
            cmd = PackCmd(OperaType.WRITE, WriteAddress);
            port.SendData(cmd,ref errMsg);
        }

        void UpdatePortRecord(StrTypeEnum type,byte[] cmd)
        {
            Thread thread = new Thread(() =>
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        PortRecordRtbx.AppendText(AddTimeStamp(type,ToHexStrFromByte(cmd))+"\r\n");
                    }));
                }
            }
            );
            thread.IsBackground = true;
            thread.Start();
        }
        void UpdateLogRecord(string str)
        {
            Thread thread = new Thread(() =>
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        currentNumber = boardNumberQueue.Dequeue();
                        ErrorLogRtbx.AppendText(string.Format("[{0}]板卡{1} {2}{3}",DateTime.Now.ToString(), currentNumber.ToString(), str, "\r\n"));
                    }));
                }
            }
            );
            thread.IsBackground = true;
            thread.Start();
        }

        void UpdateTestStatus(bool result, COMPORT.TestInfo testInfo)
        {
            Thread thread = new Thread(()=>
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        currentNumber = boardNumberQueue.Dequeue();

                        int offset = (int)(currentNumber - startNumber);

                        StatusLabel.Text = "PASS";
                        StatusLabel.BackColor = Color.LightSeaGreen;

                        //EFLASH
                        gridView.Rows[0 + offset * 4].Cells[1].Value = "EFLASH";
                        if(testInfo.EFLASH.Exception)
                            gridView.Rows[0 + offset * 4].Cells[3].Value = TestInfo.EFLASHExceptionFirstTime.ToString();
                        gridView.Rows[0 + offset * 4].Cells[4].Value = testInfo.EFLASH.Exception ? "FAIL" : "PASS";
                        gridView.Rows[0 + offset * 4].Cells[4].Style.BackColor = testInfo.EFLASH.Exception ? Color.OrangeRed : Color.GreenYellow;
                        //ROM
                        gridView.Rows[1 + offset * 4].Cells[1].Value = "ROM";
                        if (testInfo.ROM.Exception)
                        {
                            string errTypeStr = string.Empty;
                            string errDateTimeStr = string.Empty;
                            foreach (var item in TestInfo.ROMErrDict)
                            {
                                errTypeStr += item.Key + "\r\n";
                                errDateTimeStr += item.Value.ToString() + "\r\n";
                            }
                            gridView.Rows[1 + offset * 4].Cells[2].Value = errTypeStr.Trim('\n').Trim('\r');
                            gridView.Rows[3 + offset * 4].Cells[3].Value = errDateTimeStr.Trim('\n').Trim('\r');
                            gridView.Rows[1 + offset * 4].Cells[3].Value = string.Format("{0}\r\n{1}", DateTime.MaxValue == TestInfo.PROMExceptionFirstTime ? string.Empty : TestInfo.PROMExceptionFirstTime.ToString(), DateTime.MaxValue == TestInfo.DROMExceptionFirstTime ? string.Empty : TestInfo.DROMExceptionFirstTime.ToString());
                        }
                        gridView.Rows[1 + offset * 4].Cells[4].Value = testInfo.ROM.Exception ? "FAIL" : "PASS";
                        gridView.Rows[1 + offset * 4].Cells[4].Style.BackColor = testInfo.ROM.Exception ? Color.OrangeRed : Color.GreenYellow;
                        gridView.Rows[1 + offset * 4].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                        //TRIM
                        gridView.Rows[2 + offset * 4].Cells[1].Value = "TRIM";
                        gridView.Rows[2 + offset * 4].Cells[2].Value = testInfo.TRIM.Exception?string.Format("VALUE:0x{0}", testInfo.TRIMValue.ToString("X2")):string.Empty;
                        if(testInfo.TRIM.Exception)
                            gridView.Rows[2 + offset * 4].Cells[3].Value = TestInfo.TRIMExceptionFirstTime.ToString();
                        gridView.Rows[2 + offset * 4].Cells[4].Value = testInfo.TRIM.Exception ? "FAIL" : "PASS";
                        gridView.Rows[2 + offset * 4].Cells[4].Style.BackColor = testInfo.TRIM.Exception ? Color.OrangeRed : Color.GreenYellow;
                        //IP
                        gridView.Rows[3 + offset * 4].Cells[1].Value = "IP";
                        if (testInfo.IP.Exception)
                        {
                            string errTypeStr = string.Empty;
                            string errDateTimeStr = string.Empty;
                            foreach (var item in TestInfo.IPErrDict)
                            {
                                errTypeStr += item.Key + "\r\n";
                                errDateTimeStr += item.Value.ToString() + "\r\n";
                            }
                            gridView.Rows[3 + offset * 4].Cells[2].Value = errTypeStr.Trim('\n').Trim('\r');
                            //gridView.Rows[3 + offset * 4].Cells[3].Value = errDateTimeStr.Trim('\n').Trim('\r');
                            gridView.Rows[3 + offset * 4].Cells[3].Value =  TestInfo.IPExceptionFirstTime.ToString();
                        }
                        else
                            ;
                            //gridView.Rows[3 + offset * 4].Cells[3].Value = TestInfo.IPExceptionFirstTime.ToString();
                        gridView.Rows[3 + offset * 4].Cells[4].Value = testInfo.IP.Exception ? "FAIL" : "PASS";
                        gridView.Rows[3 + offset * 4].Cells[4].Style.BackColor = testInfo.IP.Exception ? Color.OrangeRed : Color.GreenYellow;
                        
                        //LABEL显示单次结果
                        if (testInfo.EFLASH.Exception ||testInfo.ROM.Exception || testInfo.TRIM.Exception || testInfo.IP.Exception)
                        {
                            StatusLabel.Text = "FAIL";
                            StatusLabel.BackColor = Color.OrangeRed;
                        }
                        //ToolTip.Show(str, this, this.PointToClient(Cursor.Position),2000);
                        //ToolTip.SetToolTip(this.OpenAllBtn, "冒泡提示");
                    }));
                }
            }
            );
            thread.IsBackground = true;
            thread.Start();
        }
       
        static byte[] FrameHeader = { 0xE5, 0x5E };
        static byte[] FrameEnd = { 0xFD, 0xFE };
        static byte[] ModeFrame = { 0x55, 0xAA, 0x66, 0xBB };
        enum OperaType
        {
            RESERVED = 0x0,
            READ = 0x1,
            WRITE = 0x2,
            REPOST = 0x5,
        }

        byte[] PackCmd(OperaType opera, byte[] content)
        {
            byte[] cmdBytes = null;
            switch(opera) {
                case OperaType.READ:
                    cmdBytes = new byte[11];
                    cmdBytes[0] = FrameHeader[0];
                    cmdBytes[1] = FrameHeader[1];
                    cmdBytes[2] = (byte)OperaType.READ;
                    content.CopyTo(cmdBytes, 3);
                    cmdBytes[7] = (byte)0x04;
                    cmdBytes[8] = FrameEnd[0];
                    cmdBytes[9] = FrameEnd[1];
                    cmdBytes = CalcCheckSum(cmdBytes);
                    break;
                case OperaType.WRITE:
                    int dataLength = content.Length;
                    cmdBytes = new byte[14];
                    cmdBytes[0] = FrameHeader[0];
                    cmdBytes[1] = FrameHeader[1];
                    cmdBytes[2] = (byte)OperaType.READ;
                    content.CopyTo(cmdBytes, 3);
                    cmdBytes[7] = (byte)0x04;
                    cmdBytes[8] = ModeFrame[0];
                    cmdBytes[9] = ModeFrame[1];
                    cmdBytes[10] = ModeFrame[2];
                    cmdBytes[11] = ModeFrame[3];
                    cmdBytes[12] = FrameEnd[0];
                    cmdBytes[13] = FrameEnd[1];
                    cmdBytes = CalcCheckSum(cmdBytes);
                    break;
                case OperaType.REPOST:
                    cmdBytes = new byte[17];
                    cmdBytes[0] = FrameHeader[0];
                    cmdBytes[1] = FrameHeader[1];
                    cmdBytes[2] = (byte)opera;
                    cmdBytes[7] = (byte)content.Length;
                    content.CopyTo(cmdBytes, 8);
                    cmdBytes[content.Length + 8] = FrameEnd[0];
                    cmdBytes[content.Length + 9] = FrameEnd[1];
                    cmdBytes = CalcCheckSum(cmdBytes);
                    break;
            }
            return cmdBytes;
        }
        byte[] CalcCheckSum(byte[] bytes)
        {
            int len = bytes.Length;
            for (int i = 0;i<len-1; i++)
            {
                bytes[len - 1] += bytes[i];
            }
            return bytes;
        }
      
        byte[] PackBoardControlCmd(byte cmd,byte num)
        {
            Byte[] cmdBytes = new Byte[6];
            cmdBytes[0] = 0x55;
            cmdBytes[1] = 0x1;
            cmdBytes[2] = cmd;
            cmdBytes[3] = num;
            cmdBytes[4] = 0x00;
            cmdBytes[5] = 0x0;
            for (int i = 0; i < 5; i++)
            {
                cmdBytes[5] += cmdBytes[i];
            }
            return cmdBytes;
        }
        public enum StrTypeEnum
        {
            SEND,
            RECV
        }
        string AddTimeStamp(StrTypeEnum type, string str)
        {
            string ts = DateTime.Now.ToString();
            return string.Format("[{0}][{1}] {2}", ts, type.ToString(), str);
        }

        public static byte[] HexStringToBytes(string hs)
        {
            string[] strArr = hs.Trim().Split(' ');
            byte[] b = new byte[strArr.Length];
            //逐个字符变为16进制字节数据
            for (int i = 0; i < strArr.Length; i++)
            {
                b[i] = Convert.ToByte(strArr[i], 16);
            }
            //按照指定编码将字节数组变为字符串
            return b;
        }
        public static string ToHexStrFromByte(byte[] byteDatas)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < byteDatas.Length; i++)
            {
                builder.Append(string.Format("{0:X2} ", byteDatas[i]));
            }
            return builder.ToString().Trim();
        }
        private void SerialPortCbx_DropDown(object sender, EventArgs e)
        {
            SerialPortCbx.Items.Clear();
            if (port.ScanComPort(2, 1, 1))
            {
                SerialPortCbx.Items.Add(port.PortName);
            }
        }

        private void SerialPortCbx_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (null != SerialPortCbx.Text)
            {
                port.Close();
                port.PortName = SerialPortCbx.Text;
                port.Open();
            }
        }
       
        bool CheckPort()
        {
            if (!port.Start())
            {
                MessageBox.Show("请先选择串口");
                return false;
            }
            return true;
        }
        //限定范围事件
        private void Number_ValueChanged(object sender, EventArgs e)
        {
            StartNumber.Maximum = EndNumber.Value;
            EndNumber.Minimum = StartNumber.Value;

            startNumber = (UInt32)StartNumber.Value;
            endNumber = (UInt32)EndNumber.Value;

            int boardCnt = (int)(EndNumber.Value - StartNumber.Value + 1);
            //TestInfo.ROMErrDict = new List<Dictionary<string, DateTime>>(boardCnt);

            DataTable dt = new DataTable();

            gridView.DataSource = dt;

            for (int i = 0; i < boardCnt * 4; i++)
            {
                dt.Rows.Add(dt.NewRow());
            }

            uint boardNumberHeader = startNumber;
            for (int i = 0; i < boardCnt; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    gridView.Rows[i * 4 + j].Cells[0].Value = boardNumberHeader;
                }
                boardNumberHeader++;
            }
        }

        private void StartTest(object sender, EventArgs e)
        {
            for (int i = 0; i < gridView.Rows.Count - 1; i++)
            {
                gridView.Rows[i].Cells[5].Value = null;
                gridView.Rows[i].Cells[5].Style.BackColor = Color.LightGoldenrodYellow;
            }

            PauseBtn.Enabled = true;
            lastTestTime = DateTime.Now;
            boardNumberQueue = new Queue<uint>();
            for (uint i = startNumber; i <= endNumber; i++)
            {
                boardNumberQueue.Enqueue(i);
            }
            for (uint i = startNumber; i <= endNumber; i++)
            {
                OpenPort(i);
                SendReadCMD();
            }
            testFlag = false;
            Thread thread = new Thread(() =>
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {
                        StartBtn.Text = "测试中";
                        StartBtn.BackColor = Color.LightGoldenrodYellow;
                        StartBtn.Enabled = false;
                    }));
                }
            }
            );
            thread.IsBackground = true;
            thread.Start();
            if(autoTestThread.ThreadState == (System.Threading.ThreadState.Suspended| System.Threading.ThreadState.Background))
                autoTestThread.Resume();

        }
        private void PauseBtn_Click(object sender, EventArgs e)
        {
            Thread thread = new Thread(() =>
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() =>
                    {

                        StartBtn.Text = "开始";
                        StartBtn.BackColor = Color.DarkSeaGreen;
                        StartBtn.Enabled = true;
                    }));
                }
            }
            );
            thread.IsBackground = true;
            thread.Start();
            autoTestThread.Suspend();

            
            for (int i = 0; i < gridView.Rows.Count-1; i++)
            {
                if (null == gridView.Rows[i].Cells[3].Value)
                {
                    gridView.Rows[i].Cells[5].Value = "PASS";
                }
                else
                {
                    gridView.Rows[i].Cells[5].Value = "FAIL";
                    gridView.Rows[i].Cells[5].Style.BackColor = Color.OrangeRed;
                }
            }
        }

        private void SavePortLog_Click(object sender, EventArgs e)
        {
            string text = PortRecordRtbx.Text;
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "串口日志|*.*";
            dialog.FileName = "串口日志" +DateTime.Now.ToString("MM-dd-HHmm");
            dialog.DefaultExt = ".txt";
            dialog.AddExtension = true;
            if(DialogResult.OK == dialog.ShowDialog())
            {
                string filePath = dialog.FileName;
                try
                {
                    StreamWriter sr = new StreamWriter(filePath);
                    sr.Write(text);
                    sr.Close();
                    Thread thread = new Thread(() =>
                    {
                        if (this.InvokeRequired)
                        {
                            this.Invoke(new Action(() =>
                            {
                                ErrorLogRtbx.AppendText(string.Format("{0}{1}", "串口日志保存成功", "\r\n"));
                            }));
                        }
                    }
                    );
                    thread.IsBackground = true;
                    thread.Start();
                }
                catch(Exception ex)
                {

                }
            }
        }

       
        private void ReadAddressTbx_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(ReadAddressTbx.Text.Trim()))
                return;
            string addressStr = ReadAddressTbx.Text.Trim();
            string pattern = @"^0x[A-Fa-f0-9]+$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(addressStr, pattern))
            {
                ReadAddressTbx.Text="0x";
                return;
            }
            addressStr = ReadAddressTbx.Text.Trim().Substring(2).PadLeft(8, '0');
            byte[] addres = HexStrToBytes(addressStr);
            ReadAddress = addres;
        }
        private void WriteAddressTbx_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(WriteAddressTbx.Text.Trim()))
                return;
            string addressStr = WriteAddressTbx.Text.Trim();
            string pattern = @"^0x[A-Fa-f0-9]+$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(addressStr, pattern))
            {
                WriteAddressTbx.Text="0x";
                return;
            }
            addressStr = WriteAddressTbx.Text.Trim().Substring(2).PadLeft(8, '0');
            byte[] addres = HexStrToBytes(addressStr);
            WriteAddress = addres;
        }

        public static byte[] HexStrToBytes(string hexString)
        {
            // 将16进制秘钥转成字节数组
            byte[] bytes = new byte[hexString.Length / 2];
            for (var x = 0; x < bytes.Length; x++)
            {
                var i = Convert.ToInt32(hexString.Substring(x * 2, 2), 16);
                bytes[x] = (byte)i;
            }
            return bytes;
        }

        private void Hour_ValueChanged(object sender, EventArgs e)
        {
            IntervalHour = Convert.ToInt32(Hour.Value);
        }

        private void Min_ValueChanged(object sender, EventArgs e)
        {
            IntervalMinute = Convert.ToInt32(Min.Value);
        }

        private void EnterTestmode(object sender, EventArgs e)
        {
            ModeFrame = new byte[4]{ 0x55, 0xAA, 0x66, 0xBB };
            for (uint i = startNumber; i <= endNumber; i++)
            {
                OpenPort(i);
                SendWriteCMD();
            }
        }

        private void ExitTestmodeBtn_Click(object sender, EventArgs e)
        {
            ModeFrame = new byte[4]{ 0x00, 0x00, 0x00, 0x00 };
            for (uint i = startNumber; i <= endNumber; i++)
            {
                OpenPort(i);
                SendWriteCMD();
            }
        }

        private void mainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            TempTool.Properties.Settings.Default.start = (int)StartNumber.Value;
            TempTool.Properties.Settings.Default.end = (int)EndNumber.Value;

            TempTool.Properties.Settings.Default.readAddress = ReadAddressTbx.Text;
            TempTool.Properties.Settings.Default.writeAddress = WriteAddressTbx.Text;
            TempTool.Properties.Settings.Default.Save();

            Dispose();
            System.Environment.Exit(0);
        }
    }
}
