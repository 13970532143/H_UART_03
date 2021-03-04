using File_HexToBin.HexAndBin;
//using Bluetooth_Fan.HardwareInfo;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

namespace STM32_Usart_Isp
{
    public partial class Form1 : Form
    {

        CFileHexBin filehexbin = new CFileHexBin();
        Stm32UsartIsp Stm32UsartIsp = new Stm32UsartIsp();
        private SerialPort fSerPort = new SerialPort();
        MCOM myCom = new MCOM();
        bool serialSta = false;
        public Form1()
        {
            InitializeComponent();
            SerPortBaud.Text = "115200";
            SerPortCom_DropDown(null, null);
            Control.CheckForIllegalCrossThreadCalls = false;//防止跨线程访问出错，好多地方会用到
            
            //fSerPort.DataReceived += new SerialDataReceivedEventHandler(port_DataReceived); 
            #region   挂载下载事件
            Stm32UsartIsp.DownErrorEventHandler += DownError; //
            Stm32UsartIsp.DownEndEventHandler += DownEnd;
            Stm32UsartIsp.DownChangeEventHandler += DownChange;
            Stm32UsartIsp.DownStartEventHandler += DownStart;
            myCom.ShowDataReceived += ShowRxData;
            #endregion
        }
        //获取串口号并显示
        private void SerPortCom_DropDown(object sender, EventArgs e)
        {
            //string[] PortName = CHardwareInfo.GetPcSerialPortName(); //得到当前设备上的串口号
            string[]  PortName = SerialPort.GetPortNames();
            SerPortCom.Items.Clear();
            if (PortName == null)
            {
                MessageBox.Show("没有找到串口");
                msgDebug.AppendText("没有找到串口" + "\r\n");
                return;
            }
            SerPortCom.Items.AddRange(PortName);
            string text = SerPortCom.Text;
            if (text == "") //如果之前的数据为空
            {
                SerPortCom.SelectedIndex = (SerPortCom.Items.Count > 0) ? 0 : SerPortCom.SelectedIndex;
            }
            else//如果之前的数据不为空
            {
                int Count = 0;
                Count = SerPortCom.Items.IndexOf(text);
                SerPortCom.SelectedIndex = Count;
            }                      
            fSerPort.BaudRate = Convert.ToInt32(SerPortBaud.Text, 10);  //得到波特率; //波特率
            //fSerPort.PortName = CHardwareInfo.GetPcSerialPortNameCom(SerPortCom.Text);   //得到串口号; //端口号
            fSerPort.Parity = Parity.Even;  //偶校验
            fSerPort.StopBits = StopBits.One;//停止位
            fSerPort.ReadBufferSize = 4096;
            fSerPort.WriteBufferSize = 4096; 
            
        }
        #region ISP
        #region 对接的委托及方法
        private void PlayLogText(string log)
        {
            if (InvokeRequired == true)   //在不同线程上调用的 必须通过 Invoke 方法对控件进行调用  
            {
                Invoke(new Action<string>(PlayLogText), new object[] { log });
            }
            else
            {
                LogText.AppendText(log + "\r\n");
            }
        }
        /// <summary>
        /// 显示下载进度
        /// </summary>
        /// <param name="All"></param>
        /// <param name="Val"></param>
        private void Playprogress(UInt32 All, UInt32 Val)
        {
            if (InvokeRequired == true)   //在不同线程上调用的 必须通过 Invoke 方法对控件进行调用  
            {
                Invoke(new Action<UInt32, UInt32>(Playprogress), new object[] { All, Val });
            }
            else
            {
                UInt32 Temp = 0;
                if ((All != 0) && (Val != 0))
                {
                    Temp = 1000 * Val / All;
                }
                progressBar1.Value = (int)(Temp / 10);  //设置进度条
                label1.Text = (Temp / 10).ToString() + "." + (Temp % 10).ToString() + "%";
            }
        }
        /// <summary>
        /// 下载出错事件
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="log"></param>
        private void DownError(object obj, string log)
        {
            //  PlayLogText("操作失败！");
            PlayLogText(log);
        }

        /// <summary>
        /// 下载开始事件
        /// </summary>
        /// <param name="obj"></param>
        private void DownStart(object obj)
        {
            Stm32UsartIsp usartisp = (Stm32UsartIsp)obj;
            PlayLogText("开始下载 \r\n串口号：" + usartisp.mSerialPort.PortName + "    波特率：" + usartisp.mSerialPort.BaudRate.ToString());
        }
        /// <summary>
        /// 下载结束事件
        /// </summary>
        /// <param name="obj"></param>
        private void DownEnd(object obj)
        {
            PlayLogText("操作完成");
        }
        /// <summary>
        /// 下载变更事件
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="AllSize"></param>
        /// <param name="NowSize"></param>
        /// <param name="str"></param>
        private void DownChange(object obj, UInt32 AllSize, UInt32 NowSize, string str)
        {
            Playprogress(AllSize, NowSize);
            if (str != "")
            {
                PlayLogText(str);
            }
        }
        #endregion



        //打开待升级文件
        private void button2_Click(object sender, EventArgs e)
        {
            openFileDialog1.CheckFileExists = true;
            openFileDialog1.CheckPathExists = true;
            openFileDialog1.FileName = "选择bin或者HEX文件";  
            openFileDialog1.Filter = "升级文件(*.bin;*.Hex;*.IHex;)|*.bin;*.Hex;*.IHex;|所有文件(*.*)|*.*";
            openFileDialog1.Title = "打开升级文件";
            if (openFileDialog1.ShowDialog(this) == DialogResult.OK)
            {
                string filePath = openFileDialog1.FileName;

                msgDebug.AppendText("打开文件:" + filePath + "\r\n");
                PlayLogText("打开文件:" + filePath);
                //装载升级文件
                if (filehexbin.Loading_OpenUpdataFile(filePath) == true)
                {
                    FilePath.Text = filePath;
                    switch (filehexbin.UpdataFileTyp)
                    {
                        case 1:  //IHEX  HEX
                            FAddr.ReadOnly = true;
                            FAddr.Text = filehexbin.FlashStaraddr.ToString("X8");
                            break;
                        case 2:   // BIN
                            FAddr.ReadOnly = false;
                            break;
                        default:
                            break;
                    }
                }
            }
        }
        //保存文件
        private void button1_Click(object sender, EventArgs e)
        {
            if (filehexbin.GetFileListDataLoading() == false)
            {
                MessageBox.Show("没有打开HEX文件");
                return;
            }
            saveFileDialog1.Filter = "升级文件(*.bin)|*.bin";
            saveFileDialog1.DefaultExt = ".Bin";
            if (saveFileDialog1.ShowDialog(this) == DialogResult.OK)
            {
                string Path = saveFileDialog1.FileName;
                Debug.WriteLine("保存文件:" + Path);
                PlayLogText("保存文件:" + Path);
                filehexbin.SaveCreateBinFile(Path);
            }
        }

        
        //开始下载
        private void button3_Click(object sender, EventArgs e)
        {
            if (Stm32UsartIsp.IsRunDownload)
            {
                Stm32UsartIsp.IsRunDownload = false;
                return;
            }
            //如果没有选择文件  且没有选择为 去除读保护
            //如果当前不为清除芯片  
            if (McuClr.Checked == false)
            {
                //则一定为下载数据   如果当前没有装载升级文件数据
                if (filehexbin.GetFileListDataLoading() == false)
                {
                    PlayLogText("请打开需要下载的文件");
                    return;
                }
                else
                {
                    filehexbin.ResetLoadingUpdataFile = true;  //重载一次升级文件数据
                }
            }
            else //当前为清除芯片数据
            {
            }
            fSerPort.BaudRate = Convert.ToInt32(SerPortBaud.Text, 10);  //得到波特率; //波特率
            //if (!fSerPort.IsOpen) fSerPort.PortName = CHardwareInfo.GetPcSerialPortNameCom(SerPortCom.Text);   //得到串口号; //端口号
            if (!fSerPort.IsOpen) fSerPort.PortName = SerPortCom.Text;   //得到串口号; //端口号
            Stm32UsartIsp.mSerialPortInit(fSerPort);
            button4_Click(null, null);  //执行清除LOG按键  
            Stm32UsartIsp.mRDPReadOut = McuClr.Checked;  //是否去除读保护
            Stm32UsartIsp.mNoEraFlash = FlashClr.Checked; //是否执行擦除FLASH操作   
            //string SerPorName = CHardwareInfo.GetPcSerialPortNameCom(SerPortCom.Text);   //得到串口号
            string SerPorName = SerPortCom.Text;   //得到串口号
            int Baud = Convert.ToInt32(SerPortBaud.Text, 10);  //得到波特率
            filehexbin.FlashStaraddr = (FAddr.Text == "") ? 0 : (UInt32)Convert.ToInt32(FAddr.Text, 16);  //得到Flash下载地址 防止刚开始没有载入地址时出错        
            Stm32UsartIsp.Download(SerPorName, Baud, filehexbin.DataList.ToArray(), filehexbin.FlashStaraddr);
        }


        /// <summary>
        /// 限制文本框只能输入十六进制数 及退格按键
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FAddr_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = ("0123456789ABCDEF".IndexOf(char.ToUpper(e.KeyChar)) < 0) && (e.KeyChar != (char)Keys.Back);
        }
        //清除LOG
        private void button4_Click(object sender, EventArgs e)
        {
            LogText.Clear();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void SerPortCom_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
        #endregion

        //private void port_DataReceived(object sender, EventArgs e)
        //{ 

        //}
        #region 串口助手
        private void ShowRxData(object obj, string log)
        {
            textBox1.AppendText(log);
 
        }
        //打开串口
        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                string SerPorName = SerPortCom.Text;   //得到串口号
                int Baud = Convert.ToInt32(SerPortBaud.Text, 10);  //得到波特率         
                //未打开串口
                if (serialSta == false)
                {
                    fSerPort.BaudRate = Baud; //波特率
                    fSerPort.PortName = SerPorName; //端口号
                    if (!fSerPort.IsOpen)
                    {
                        fSerPort.Open();
                        button5.Text = "关闭串口";
                        SerPortCom.Enabled = false;
                        SerPortBaud.Enabled = false;
                        serialSta = true;
                        msgDebug.AppendText("打开串口:" + SerPorName + "\r\n");

                    }
                }
                else //串口已打开
                {
                    if (fSerPort.IsOpen)
                    {
                        fSerPort.Close();
                        button5.Text = "打开串口";
                        SerPortCom.Enabled = true;
                        SerPortBaud.Enabled = true;
                        serialSta = false;
                        msgDebug.AppendText("关闭串口" + SerPorName + "\r\n");
                    }
                }
            }
            catch 
            {
                MessageBox.Show("串口操作失败");
            }
        }
        
        //点击发送数据
        private void button6_Click(object sender, EventArgs e)
        {
            
            if (fSerPort.IsOpen)
            {
                string txStr;
                txStr = textBox2.Text.ToString();
                if (checkBox3.Checked)
                {
                    txStr = textBox2.Text.ToString()+ "\r\n";
                }

                myCom.Send_Mes(txStr,checkBox1.Checked);

            }
            else 
            {
                MessageBox.Show("发送失败，串口未打开");
 
            }
        }

        #endregion

        private void SerialClose()
        {
            if (fSerPort.IsOpen == true)
            {
                fSerPort.Close();
                //fSerPort.Dispose();  //释放资源
                button5.Text = "打开串口";
                SerPortCom.Enabled = true;
                SerPortBaud.Enabled = true;
                serialSta = false;
                myCom.mySerail = fSerPort;
            }
           
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (tabControl1.SelectedIndex)
            {
                case 0:
                    msgDebug.AppendText("ISP\r\n");
                    SerialClose();
                    Stm32UsartIsp.CloseSeraPort();
                    break;
                case 1:
                    SerialClose();
                    Stm32UsartIsp.CloseSeraPort();
                    msgDebug.AppendText("串口助手\r\n");
                    break;
                default: break;
            }
        }
    }
}
