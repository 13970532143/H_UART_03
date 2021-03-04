using File_HexToBin.HexAndBin;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

namespace STM32_Usart_Isp
{
    public struct Stm32MCU_inof
    {
        public bool Completion;
        public UInt16 FlashSize;
        public byte[] Only96bitID;
        public byte[] Cmm_ID;
        public string Device_ID_Code;
        public UInt16 Pid;
        public byte BootloaderVer;
        public byte[] OptionBytes;
    }
    class Stm32UsartIsp
    {
        /// <summary>
        /// MCU 应答字节
        /// </summary>
        public static readonly byte MCU_ACK = (byte)0x79;
        /// <summary>
        /// MCU 非应答字节
        /// </summary>
        public static readonly byte MCU_NACK = (byte)0x1F;

        /// <summary>
        /// 每次下载给MCU的数据块大小  必须为4的整数倍 最大256
        /// </summary>
        public static readonly UInt16 DownBlockSize = 256;

        /// <summary>
        /// 串口
        /// </summary>
        public SerialPort mSerialPort { get; private set; }

        /// <summary>
        /// STM32 MCU 相关参数
        /// </summary>
        private Stm32MCU_inof _stm32_inof;
        public Stm32MCU_inof stm32_inof
        {
            get
            {
                return _stm32_inof;
            }
        }

        private bool _mRDPReadOut = false;
        /// <summary>
        /// 是否执行去除读保护 操作完成后直接退出    真执行去读保护
        /// </summary>
        public bool mRDPReadOut
        {
            get { return _mRDPReadOut; }
            set
            {
                if (_mRDPReadOut == value)
                {
                    return;
                }
                _mRDPReadOut = value;
            }
        }

        private bool _mNoEraFlash = true;
        /// <summary>
        /// 是否执行擦除FLASH操作 true 执行
        /// </summary>
        public bool mNoEraFlash
        {
            get { return _mNoEraFlash; }
            set
            {
                if (_mNoEraFlash == value)
                {
                    return;
                }
                _mNoEraFlash = value;
            }
        }

        /// <summary>
        /// 需要下载的数据
        /// </summary>
        public Byte[] mDataBuff { get; private set; }

        /// <summary>
        /// Flash 的起始地址 及跳转地址
        /// </summary>
        public UInt32 mFlashAddr { get; private set; }

        /// <summary>
        /// 下载线程
        /// </summary>
        private Thread mThread;

        /// <summary>
        /// 当前选择的下载文件的目录
        /// </summary>
        public string FilePath { get; private set; }

        #region   触发的事件
        /// <summary>
        /// 开始运行下载线程 事件
        /// </summary> 
        public event Action<object> DownStartEventHandler;

        private void OnDownStartEventHandler()
        {
            if (DownStartEventHandler != null)
            {
                DownStartEventHandler(this);
            }
        }

        /// <summary>
        /// 下载错误事件  string  为错误信息
        /// </summary>
        public event Action<object, string> DownErrorEventHandler;
        private void OnDownErrorEventHandler(string str)
        {
            try
            {
                if (mSerialPort.BytesToRead > 0)
                {
                    byte[] buff = new byte[mSerialPort.BytesToRead];
                    mSerialPort.Read(buff, 0, buff.Length);
                    Debug.WriteLine("串口中还有" + buff.Length + "字节数据");
                    Debug.WriteLine(ToHexString(buff));
                }
            }
            catch (Exception)
            {

            }

            if (DownErrorEventHandler != null)
            {
                DownErrorEventHandler(this, str);
            }
        }
        /// <summary>
        /// 下载进度发生了变化 事件
        /// </summary>
        public event Action<object, UInt32, UInt32, string> DownChangeEventHandler;

        /// <summary>
        /// 与DownChangeEventHandler 事件对接
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="AllSize">总大小</param>
        /// <param name="NowSize">当前执行大小</param>
        /// <param name="str">提示信息</param>
        private void OnDownChangeEventHandler(UInt32 AllSize, UInt32 NowSize, string str)
        {
            if (DownChangeEventHandler != null)
            {
                DownChangeEventHandler(this, AllSize, NowSize, str);
            }
        }

        /// <summary>
        /// 下载完成事件
        /// </summary>
        public event Action<object> DownEndEventHandler;

        private void OnDownEndEventHandler()
        {
            if (DownEndEventHandler != null)
            {
                DownEndEventHandler(this);
            }
        }
        #endregion




        /// <summary>
        /// 检查当前是否在运行下载   运行为真
        /// </summary>
        /// <returns>真 当前正在执行 </returns>
        private bool _IsRunDownload = false;

        public bool IsRunDownload
        {
            get
            {
                return _IsRunDownload;
            }
            set
            {
                if ((IsRunDownload == true) && (value == false))
                {
                    CloseSeraPort(); //关闭串口
                    OnDownErrorEventHandler("停止操作");
                }
            }
        }
        /// <summary>
        /// 打开串口
        /// </summary>
        public bool OpenSeraPort()
        {
            try
            {
                if (mSerialPort.IsOpen == false)
                {
                    mSerialPort.Open();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public bool CloseSeraPort()
        {
            try
            {
                if (mSerialPort.IsOpen == true)
                {
                    mSerialPort.Close();
                    //mSerialPort.Dispose();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        /// <summary>
        /// 异或校验
        /// </summary>
        /// <param name="Data"></param>
        /// <returns></returns>
        public static byte Return_Xor(byte[] Data)
        {
            byte Xor = 0;
            foreach (byte item in Data)
            {
                Xor ^= item;
            }
            return Xor;
        }


        ////串口初始化
        public void mSerialPortInit(SerialPort serial)
        {
            mSerialPort = serial;  //实例化
        }
        /// <summary>
        /// 下载
        /// </summary>
        /// <param name="portName">串口号</param>
        /// <param name="baudRate">波特率</param>
        /// <param name="Data">需要下载的HEX 数据</param>
        /// <param name="FlashAddr">FLASH写地址</param>
        /// <returns></returns>
        public bool Download(string portName, int baudRate, Byte[] Data, UInt32 FlashAddr)
        {
            if (IsRunDownload)  //如果还没有下载完成  则直接退出
            {
                return false;
            }
            OpenSeraPort();
            mDataBuff = Data;
            //如果当前不为去清除芯片操作   则FLASH数据大小不能为0
            if ((mRDPReadOut == false) && (mDataBuff.Length == 0))
            {
                return false;
            }
            mFlashAddr = FlashAddr;
            mThread = new Thread(DownLoad_Thread);
            mThread.IsBackground = true;  //后台线程
            mThread.Start(this);
            _stm32_inof.Completion = false;
            return true;
        }

        /// <summary>
        /// 下载线程
        /// </summary>
        /// <param name="obj"></param>
        private void DownLoad_Thread(object obj)
        {
            try
            {
                if (mSerialPort.IsOpen == false)
                {
                    OnDownErrorEventHandler("串口打开失败");
                    return;
                }
            }
            catch
            {
                MessageBox.Show("串口未打开");
                return;
            }
            OnDownStartEventHandler();            
            //如果需要下载的数据不为空 且 不是清除芯片操作
            if ((mDataBuff != null) && (mRDPReadOut == false))
            {
                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "需要下载的FLASH数据大小: " + mDataBuff.Length + " 字节");
            }

            _IsRunDownload = true;  //标记线程下载在进行
            do
            {
                if (Set_Mcu_to_Isp() == false)
                {
                    OnDownErrorEventHandler("MCU进入ISP模式失败");
                    break;
                }

                if (mRDPReadOut == true)  //如果是擦除读保护 则操作完成后 就退出
                {
                    if (Readout_Unprotect_command() == true) //执行去除读保护   执行成功后MCU会复位
                    {
                        if (Set_Mcu_to_Isp() == false)  //需要重新进入ISP模式
                        {
                            OnDownErrorEventHandler("MCU进入ISP模式失败");
                        }
                        else  //进入ISP成功
                        {
                            if (Erase_Memory_command() == false)
                            {
                                OnDownErrorEventHandler("擦除FLASH命令执行失败");
                            }
                            else
                            {
                                OnDownEndEventHandler();//下载完成事件
                                
                            }
                        }
                    }
                    else
                    {
                        OnDownErrorEventHandler("去除读保护失败");
                    }
                    break;  //当前操作完成  退出线程
                }
                if (Get_ID_command() == false)
                {
                    OnDownErrorEventHandler("GET ID 命令失败 可能设置了读保护");
                    break;
                }
                if (Get_Version_command() == false)
                {
                    OnDownErrorEventHandler("读版本命令失败");
                    break;
                }

                if (mNoEraFlash) //如果要执行擦除FLASH操作
                {
                    if (Erase_Memory_command() == false)
                    {
                        OnDownErrorEventHandler("擦除FLASH命令执行失败");
                        break;
                    }
                    else  //如果执行擦除命令成功了   还要判断是否为延伸擦除命令 执行此命令后 需要重新进入ISP模式
                    {
                        if (_stm32_inof.Cmm_ID[6] == 0X44)  //如果为延伸擦除命令 如果执行擦除成功  则要判断是否为延伸擦除命令  如果是 则需要重新配置MCU进入ISP
                        {
                            if (Set_Mcu_to_Isp() == false)
                            {
                                OnDownErrorEventHandler("MCU进入ISP模式失败");
                                break;
                            }

                            if (Get_ID_command() == false)
                            {
                                OnDownErrorEventHandler("GET ID 命令失败");
                                break;
                            }
                            if (Get_Version_command() == false)
                            {
                                OnDownErrorEventHandler("读版本命令失败");
                                break;
                            }
                        }
                    }
                }
                int Error = 0;
                if ((Error = DownLoadFlashDataAndGo()) != 0)
                {
                    string str = "";
                    switch (Error)
                    {
                        case 1:
                            str = "Write Memory Command命令错误";
                            break;
                        case 2:
                            str = "Read Memory Command命令错误";
                            break;
                        case 3:
                            str = "写入与读出数据不相等  可能是FLASH没有擦除  请勾选擦除FLASH";
                            break;
                        case 4:
                            str = "GO命令执行失败";
                            break;
                        default:
                            break;
                    }
                    OnDownErrorEventHandler(str);
                    break;
                }
                _stm32_inof.Completion = true;   //标记信息获取完成
                //OnDownEndEventHandler();//下载完成事件
            } while (false);
            CloseSeraPort();
            _IsRunDownload = false;  //标记线程下载完成
        }

        /// <summary>
        /// 发送命令 并接收返回数据
        /// </summary>
        /// <param name="CMM">命令</param>
        /// <param name="RecData">接收到的数据</param>
        /// <param name="Count">超时时间</param>
        /// <returns></returns>
        public bool Usart_SendCmmRec(byte[] CMM, ref byte[] RecData, int Count, int timeout = 1000)
        {
            lock (this)
            {
                Byte[] DataBuff = new byte[1024];
                int Index = 0;  //当前接收到数据的偏移
                if (mSerialPort.IsOpen == false)  //如果断开了  直接退出
                {
                    return false;
                }
                try
                {
                    Debug.WriteLine("发送数据{0}个字节：\r\n" + ToHexString(CMM), CMM.Length);
                    mSerialPort.Write(CMM, 0, CMM.Length);  //发送命令
                    do
                    {
                        var IAs = mSerialPort.BaseStream.BeginRead(DataBuff, Index, 1024 - Index, null, null);
                        if (IAs.AsyncWaitHandle.WaitOne(timeout, false) == true) //接收到信号
                        {
                            int Length = mSerialPort.BaseStream.EndRead(IAs);  //得到此次接收到的数据
                            Index += Length;  //缓存偏移
                            Thread.Sleep(1);
                            int SerCount = mSerialPort.BytesToRead;  //读取串口缓存中数据大小
                            mSerialPort.Read(DataBuff, Index, SerCount);
                            Index += SerCount;  //缓存偏移
                            //打印调试信息 --------------------------
                            byte[] Debugbuff = new byte[Index];
                            Array.Copy(DataBuff, Debugbuff, Index);
                            Debug.WriteLine("单次接收数据长度{0}：\r\n" + ToHexString(Debugbuff), Debugbuff.Length);
                            //-----------------------------------------
                            if (Index >= Math.Abs(Count))
                            {
                                RecData = new byte[Index];
                                Array.Copy(DataBuff, RecData, Index);
                                Debug.WriteLine("接收数据：\r\n" + ToHexString(RecData));
                                return true;
                            }
                            continue;
                        }
                        else
                        {
                            return false;
                        }
                    } while (true);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 配置MCU  BOOT0引脚电平及MCU复位
        /// </summary>
        /// <returns></returns>
        public bool Set_Mcu_bootIO_toIsp()
        {
            //配置串口引脚的RTS及 DTR 引脚  使MCU进入ISP状态
            //RTS与RTS# 是反逻辑    DTR与DTR# 是反逻辑   即控制RTS引脚输出高电平则RTS#引脚输出低电平
            try
            {
                //配置DTR#输出高电平   MCU复位
                mSerialPort.DtrEnable = false;
                Debug.WriteLine("DTR#输出高电平 MCU复位 延时100ms");
                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "DTR#输出高电平 MCU复位 延时100ms");
                Thread.Sleep(100);

                //配置RTS#输出低电平   使BOOT0引脚为高电平   关闭串口后自动会为低电平
                mSerialPort.RtsEnable = true;
                Debug.WriteLine("RTS#拉低 BOOT0为高电平 延时100ms");
                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "RTS#拉低 BOOT0为高电平 延时100ms");
                Thread.Sleep(100);

                //配置DTR#输出低电平  释放MCU复位
                mSerialPort.DtrEnable = true;
                Debug.WriteLine("DTR#输出低电平 释放复位 延时100ms");
                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "DTR#输出低电平 释放复位 延时100ms");
                Thread.Sleep(100);

                //配置RTS#输出高电平   使MCU BOOT0引脚为低电平
                mSerialPort.RtsEnable = false;
                Debug.WriteLine("RTS#输出高电平  boot0为低电平");
                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "RTS#输出高电平  boot0为低电平");
                Thread.Sleep(100);
                return true;
            }
            catch (Exception e)
            {
                if (DownErrorEventHandler != null)
                {
                    DownErrorEventHandler(this, e.Message);
                }
                return false;
            }
        }

        /// <summary>
        /// 设置MCU进入ISP模式
        /// </summary>
        /// <returns></returns>
        public bool Set_Mcu_to_Isp()
        {
            int CountAdd = 0;
            if (Set_Mcu_bootIO_toIsp() == false)   //配置MCU进入 ISP 模式
            {
                return false;
            }
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "正在连接......");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送波特率自动同步字符0X7F ");
            byte[] Sendbuf = new byte[] { 0X7F };
            byte[] Recbuf = new byte[1];
            while ((CountAdd < 500) && (mSerialPort.IsOpen))  //串口打开且连接没有超时
            {
                if (Usart_SendCmmRec(Sendbuf, ref Recbuf, 1, 300))  //发送命令
                {
                    if ((Recbuf[0] == MCU_ACK) || (Recbuf[0] == MCU_NACK))
                    {
                        if (Get_command() == true)  //检查是否正确进入ISP 模式
                        {
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Get Command命令");
                            string Optionstr = "";
                            foreach (Byte item in _stm32_inof.Cmm_ID)
                            {
                                Optionstr += "0X" + item.ToString("X2") + " ";
                            }
                            Optionstr = "支持的命令集:" + Optionstr;
                            Debug.WriteLine(Optionstr);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Optionstr);
                            return true;
                        }
                        else
                        {
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "同步波特率失败 重新同步");
                            if (Set_Mcu_bootIO_toIsp() == false)   //配置MCU进入 ISP 模式
                            {
                                return false;
                            }
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送波特率自动同步字符0X7F ");
                        }
                    }
                }
                CountAdd++;
            }
            if (CountAdd >= 500)
            {
                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "同步波特率超时");
            }
            return false;
        }

        /// <summary>
        /// 返回版本 及支持的命令
        /// </summary>
        /// <returns></returns>
        public bool Get_command()
        {
            byte[] Sendbuf = new byte[] { 0X00, 0XFF };
            byte[] Recbuf = new byte[1];
            Debug.WriteLine("发送Get Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, 15))
            {
                if ((Recbuf[0] == Recbuf[14]) && (Recbuf[14] == MCU_ACK) && (Recbuf[1] == (byte)(15 - 3 - 1)))
                {
                    _stm32_inof.Cmm_ID = new byte[11];
                    Array.Copy(Recbuf, 3, _stm32_inof.Cmm_ID, 0, 11); //拷贝支持的命令
                    _stm32_inof.BootloaderVer = Recbuf[3];  //得到bootloader 版本
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 返回版本 及 选项字节
        /// </summary>
        /// <returns></returns>
        public bool Get_Version_command()
        {
            byte[] Sendbuf = new byte[] { 0X01, 0XFE };
            byte[] Recbuf = new byte[1];
            Debug.WriteLine("发送Get Version Command命令");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Get Version Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, 5))
            {
                byte[] mOptionByte = new byte[2];
                if ((Recbuf[0] == Recbuf[4]) && (Recbuf[4] == MCU_ACK))
                {
                    Array.Copy(Recbuf, 2, mOptionByte, 0, mOptionByte.Length); //拷贝2个选项字节
                    string Optionstr = "";
                    foreach (Byte item in mOptionByte)
                    {
                        Optionstr += "0X" + item.ToString("X2") + " ";
                    }
                    Debug.WriteLine("两字节选项字节：" + Optionstr);
                    OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "两字节选项字节：" + Optionstr);

                    _stm32_inof.BootloaderVer = Recbuf[1]; //得到BOOTLOADER 版本   
                    Debug.WriteLine("Bootloader 版本" + (_stm32_inof.BootloaderVer / 16).ToString() + "." + (_stm32_inof.BootloaderVer % 16).ToString());
                    OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "Bootloader 版本:" + "V" + (_stm32_inof.BootloaderVer / 16).ToString() + "." + (_stm32_inof.BootloaderVer % 16).ToString());
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 得到PID
        /// </summary>
        /// <returns></returns>
        public bool Get_ID_command()
        {
            byte[] Sendbuf = new byte[] { 0X02, 0XFD };
            byte[] Recbuf = new byte[1];
            int Recsize = 5;  //接收的数据长度
            Debug.WriteLine("发送Get ID Command命令");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Get ID Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize, 200))
            {
                if ((Recbuf[0] == Recbuf[4]) && (Recbuf[4] == MCU_ACK) && (Recbuf[1] == ((byte)(Recsize - 3 - 1))))
                {
                    //得到PID
                    _stm32_inof.Pid = (UInt16)((Recbuf[2] << 8) | Recbuf[3]);
                    UInt32 ReadAddr = 0X1FFFF7CC;
                    string Debuglog;
                    //参考对应数据手册  DBG章节
                    switch (_stm32_inof.Pid)
                    {
                        //STM32F0XX
                        case 0X444:
                        case 0X440:
                        case 0X445:
                        case 0X448:
                        case 0X442:
                            switch (_stm32_inof.Pid)
                            {
                                case 0X444:
                                    _stm32_inof.Device_ID_Code = "STM32F030X4 or STM32F030X6  Revision Code A or 1 Revision Number 1.0 \r\nREV_ID 0X1000";
                                    break;
                                case 0X445:
                                    _stm32_inof.Device_ID_Code = "STM32F070X6  Revision Code A Revision Number 1.0 \r\nREV_ID 0X1000";
                                    break;
                                case 0X440:
                                    _stm32_inof.Device_ID_Code = "STM32F030X8  Revision Code B or 1 Revision Number 1.1 \r\nREV_ID 0X1001";
                                    break;
                                case 0X448:
                                    _stm32_inof.Device_ID_Code = "STM32F070XB  Revision Code Y or 1 Number 2.1 \r\nREV_ID 0X2001";
                                    break;
                                case 0X442:
                                    _stm32_inof.Device_ID_Code = "STM32F070XB  Revision Code A Number 1.0 \r\nREV_ID 0X1000";
                                    break;
                            }
                            Debuglog = "PID为： 0X" + _stm32_inof.Pid.ToString("X4") + "  " + _stm32_inof.Device_ID_Code;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //读取96位唯一ID -------------------------------------------
                            Recbuf = new byte[12];
                            ReadAddr = 0X1FFFF7AC;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读96位唯一ID错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            _stm32_inof.Only96bitID = new byte[12];
                            Array.Copy(Recbuf, _stm32_inof.Only96bitID, _stm32_inof.Only96bitID.Length);
                            Debuglog = "";
                            foreach (var item in _stm32_inof.Only96bitID)
                            {
                                Debuglog += item.ToString("X2");
                            }
                            Debuglog = "96位唯一ID：" + Debuglog;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //读取FLASH大小 ----------------------------------
                            Recbuf = new byte[2];
                            ReadAddr = 0X1FFFF7CC;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读FLASH大小错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            //_stm32_inof.FlashSize= (uint16)(Recbuf[1] << 8 + Recbuf[0]);
                            _stm32_inof.FlashSize = BitConverter.ToUInt16(Recbuf, 0);
                            Debuglog = "FLASH大小:" + _stm32_inof.FlashSize + "KB";
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //读取选项字节 --------------------------------------
                            Recbuf = new byte[16];
                            ReadAddr = 0x1FFFF800;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读选项字节错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            _stm32_inof.OptionBytes = new byte[Recbuf.Length];
                            //拷贝选项字节
                            Array.Copy(Recbuf, _stm32_inof.OptionBytes, _stm32_inof.OptionBytes.Length);
                            Debuglog = "";
                            foreach (var item in _stm32_inof.OptionBytes)
                            {
                                Debuglog += item.ToString("X2");
                            }
                            Debuglog = "选项字节: " + Debuglog;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            break;
                        //STM32F4
                        case 0X413:
                        case 0X419:
                            switch (_stm32_inof.Pid)
                            {
                                case 0X413:
                                    _stm32_inof.Device_ID_Code = "STM32F405XX/407XX or STM32F415XX/417XX";
                                    break;
                                case 0X419:
                                    _stm32_inof.Device_ID_Code = "STM32F42XXX or STM32F43XXX";
                                    break;
                            }
                            Debuglog = "PID为： 0X" + _stm32_inof.Pid.ToString("X4") + "  " + _stm32_inof.Device_ID_Code;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //读取96位唯一ID -------------------------------------------
                            //唯一ID地址为 0X1FFF7A10  长度12字节   由于发送读取FLASH命令时  此地址返回非应答   
                            //故采用偏移地址方式得到 96位唯一ID(地址0X1FFF7A10) 及 FLASH 大小 (地址0X1FFF7A22) 
                            Recbuf = new byte[128];
                            ReadAddr = 0X1FFF7A00;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读STM32F4参数错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            _stm32_inof.Only96bitID = new byte[12];
                            Array.Copy(Recbuf, 16, _stm32_inof.Only96bitID, 0, _stm32_inof.Only96bitID.Length);
                            Debuglog = "";
                            foreach (var item in _stm32_inof.Only96bitID)
                            {
                                Debuglog += item.ToString("X2");
                            }
                            Debuglog = "96位唯一ID：" + Debuglog;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //读取FLASH大小 ----------------------------------
                            //_stm32_inof.FlashSize= (uint16)(Recbuf[0x23] << 8 + Recbuf[0x22]);
                            _stm32_inof.FlashSize = BitConverter.ToUInt16(Recbuf, 0X22);
                            Debuglog = "FLASH大小:" + _stm32_inof.FlashSize + "KB";
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);

                            //读取选项字节 --------------------------------------
                            Recbuf = new byte[16];
                            ReadAddr = 0X1FFFC000;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读选项字节错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            _stm32_inof.OptionBytes = new byte[Recbuf.Length];
                            //拷贝选项字节
                            Array.Copy(Recbuf, _stm32_inof.OptionBytes, _stm32_inof.OptionBytes.Length);
                            Debuglog = "";
                            foreach (var item in _stm32_inof.OptionBytes)
                            {
                                Debuglog += item.ToString("X2");
                            }
                            Debuglog = "选项字节: " + Debuglog;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //此处代码未测试  
                            //if (_stm32_inof.Pid == 0X0419)  //STM32F42XXX or STM32F43XXX
                            //{
                            //    Recbuf = new byte[16];
                            //    ReadAddr = 0X1FFEC000;
                            //    if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            //    {
                            //        Debuglog = "读选项字节错误，出错地址：0X" + ReadAddr.ToString("X8");
                            //        Debug.WriteLine(Debuglog);
                            //        OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //        return false;
                            //    }
                            //    _stm32_inof.OptionBytes = new byte[Recbuf.Length];
                            //    //拷贝选项字节
                            //    Array.Copy(Recbuf, _stm32_inof.OptionBytes, _stm32_inof.OptionBytes.Length);
                            //    Debuglog = "";
                            //    foreach (var item in _stm32_inof.OptionBytes)
                            //    {
                            //        Debuglog += item.ToString("X2");
                            //    }
                            //    Debuglog = "选项字节: " + Debuglog;
                            //    Debug.WriteLine(Debuglog);
                            //    OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //}
                            break;
                        case 0X412:
                        case 0X410:
                        case 0X414:
                        case 0X418:
                            switch (_stm32_inof.Pid)
                            {
                                case 0X412:
                                    _stm32_inof.Device_ID_Code = "STM32F10X 小容量";
                                    break;
                                case 0X410:
                                    _stm32_inof.Device_ID_Code = "STM32F10X 中容量";
                                    break;
                                case 0X414:
                                    _stm32_inof.Device_ID_Code = "STM32F10X 大容量";
                                    break;
                                case 0X418:
                                    _stm32_inof.Device_ID_Code = "STM32F10X 互联系列";
                                    break;
                            }
                            Debuglog = "PID为： 0X" + _stm32_inof.Pid.ToString("X4") + "  " + _stm32_inof.Device_ID_Code;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //读取96位唯一ID -------------------------------------------
                            Recbuf = new byte[128];
                            ReadAddr = 0X1FFFF7E8;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读STM32F4参数错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            _stm32_inof.Only96bitID = new byte[12];
                            Array.Copy(Recbuf, 16, _stm32_inof.Only96bitID, 0, _stm32_inof.Only96bitID.Length);
                            Debuglog = "";
                            foreach (var item in _stm32_inof.Only96bitID)
                            {
                                Debuglog += item.ToString("X2");
                            }
                            Debuglog = "96位唯一ID：" + Debuglog;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            //读取FLASH大小 ----------------------------------
                            Recbuf = new byte[2];
                            ReadAddr = 0X1FFFF7E0;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读FLASH大小错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            //_stm32_inof.FlashSize= (uint16)(Recbuf[1] << 8 + Recbuf[0]);
                            _stm32_inof.FlashSize = BitConverter.ToUInt16(Recbuf, 0);
                            Debuglog = "FLASH大小:" + _stm32_inof.FlashSize + "KB";
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);

                            //读取选项字节 --------------------------------------
                            Recbuf = new byte[16];
                            ReadAddr = 0X1FFFF800;
                            if (Read_Memory_command(ReadAddr, Recbuf) == false)
                            {
                                Debuglog = "读选项字节错误，出错地址：0X" + ReadAddr.ToString("X8");
                                Debug.WriteLine(Debuglog);
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                                return false;
                            }
                            _stm32_inof.OptionBytes = new byte[Recbuf.Length];
                            //拷贝选项字节
                            Array.Copy(Recbuf, _stm32_inof.OptionBytes, _stm32_inof.OptionBytes.Length);
                            Debuglog = "";
                            foreach (var item in _stm32_inof.OptionBytes)
                            {
                                Debuglog += item.ToString("X2");
                            }
                            Debuglog = "选项字节: " + Debuglog;
                            Debug.WriteLine(Debuglog);
                            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, Debuglog);
                            break;
                        default:
                            break;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 读数据  必须为4的倍数
        /// </summary>
        /// <param name="addr"></param>
        /// <param name="BUFF"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        public bool Read_Memory_command(UInt32 addr, byte[] BUFF)
        {
            byte[] Sendbuf = new byte[] { 0X11, 0XEE };
            byte[] Recbuf = new byte[1];
            int Recsize = 1;  //接收的数据长度
            Debug.WriteLine("发送Read Memory Command命令");
            //     Realize_DownChangeEventHandler(this, (UInt32)mDataBuff.LongLength, 0, "发送Read Memory Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
            {
                if (Recbuf[0] == MCU_ACK)
                {
                    Sendbuf = new byte[5];
                    Sendbuf[0] = (byte)((addr & 0xff000000) >> 24);
                    Sendbuf[1] = (byte)((addr & 0x00ff0000) >> 16);
                    Sendbuf[2] = (byte)((addr & 0x0000ff00) >> 8);
                    Sendbuf[3] = (byte)(addr & 0x000000ff);
                    byte[] Data = new byte[4];  //用于计算异或值
                    Array.Copy(Sendbuf, 0, Data, 0, 4);
                    Sendbuf[4] = Return_Xor(Data);  //得到异或校验值
                    if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize)) //发送地址
                    {
                        if (Recbuf[0] == MCU_ACK)
                        {
                            Sendbuf = new byte[2];
                            Sendbuf[0] = (byte)(BUFF.Length - 1); //发送的数据为实际接收数据长度 -1
                            Sendbuf[1] = (byte)(Sendbuf[0] ^ 0xff);
                            Recsize = BUFF.Length + 1; //需要接收数据长度  第一个字节为ACK后面紧接着为数据
                            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize)) //发送读数据数量
                            {
                                if (Recbuf[0] == MCU_ACK)
                                {
                                    Array.Copy(Recbuf, 1, BUFF, 0, BUFF.Length);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }


        /// <summary>
        /// 写指定地址数据  长度最大为256   长度必须为4的倍数
        /// </summary>
        /// <param name="addr"></param>
        /// <param name="BUFF"></param>
        /// <returns></returns>
        public bool Write_Memory_command(UInt32 addr, byte[] BUFF)
        {
            byte[] Sendbuf = new byte[] { 0X31, 0XCE };
            byte[] Recbuf = new byte[1];
            int Recsize = 1;  //接收的数据长度
            Debug.WriteLine("发送Write Memory Command命令");
            //   Realize_DownChangeEventHandler(this, (UInt32)mDataBuff.LongLength, 0, "发送Write Memory Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
            {
                if (Recbuf[0] == MCU_ACK)
                {
                    Sendbuf = new byte[5];
                    Sendbuf[0] = (byte)((addr & 0xff000000) >> 24);
                    Sendbuf[1] = (byte)((addr & 0x00ff0000) >> 16);
                    Sendbuf[2] = (byte)((addr & 0x0000ff00) >> 8);
                    Sendbuf[3] = (byte)(addr & 0x000000ff);
                    byte[] Data = new byte[4];  //用于计算异或值
                    Array.Copy(Sendbuf, Data, 4);
                    Sendbuf[4] = Return_Xor(Data);  //得到异或校验值
                    if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize)) //发送地址
                    {
                        if (Recbuf[0] == MCU_ACK)
                        {
                            Sendbuf = new byte[BUFF.Length + 2];  //发送的数据要多加2个字节  1个字节为长度  1个字节为CRC
                            Sendbuf[0] = (byte)(BUFF.Length - 1); //发送的数据为实际需要些的数据长度 -1
                            Array.Copy(BUFF, 0, Sendbuf, 1, BUFF.Length); //拷贝需要发送的数据
                            Data = new byte[BUFF.Length + 1];         //用于计算CRC
                            Array.Copy(Sendbuf, Data, Data.Length);  //拷贝需要计算的数据
                            Sendbuf[Sendbuf.Length - 1] = Return_Xor(Data);  //得到异或校验值
                            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize)) //发送读数据数量
                            {
                                if (Recbuf[0] == MCU_ACK)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 擦除FLASH命令 擦除所有数据 里面包含了延伸擦除命令
        /// </summary>
        /// <returns></returns>
        public bool Erase_Memory_command()
        {
            byte[] Sendbuf = new byte[] { 0X43, 0XBC };
            byte[] Recbuf = new byte[1];
            int Recsize = 1;  //接收的数据长度
            Debug.WriteLine("发送Erase Memory Command命令");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Erase Memory Command命令");
            Sendbuf[0] = _stm32_inof.Cmm_ID[6]; //支持的擦除命令
            Sendbuf[1] = (byte)(Sendbuf[0] ^ 0xff);
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
            {
                if (Recbuf[0] == MCU_ACK)
                {
                    if (_stm32_inof.Cmm_ID[6] == 0X43) //擦除命令
                    {
                        Sendbuf[0] = 0XFF;
                        Sendbuf[1] = 0x00;
                        if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
                        {
                            if (Recbuf[0] == MCU_ACK)
                            {
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "Flash数据擦除完成");
                                return true;
                            }
                        }
                    }
                    else if (_stm32_inof.Cmm_ID[6] == 0X44)//延伸擦除命令
                    {
                        Sendbuf = new byte[4];
                        Sendbuf[0] = 0XFF;
                        Sendbuf[1] = 0XFF;
                        Sendbuf[2] = 0X00;
                        Sendbuf[3] = 0X00;
                        OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "擦除时间较长 请等待....");
                        if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize, 60 * 1000))  //发送命令
                        {
                            if (Recbuf[0] == MCU_ACK)
                            {
                                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "Flash数据擦除完成");
                                return true;
                            }
                        }
                    }
                    else
                    {
                        OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "返回的指令集错误");
                        return false;
                    }
                }
            }
            return false;
        }
        /// <summary>
        /// 跳转命令
        /// </summary>
        /// <param name="addr"></param>
        /// <returns></returns>
        public bool Go_command(UInt32 addr)
        {
            byte[] Sendbuf = new byte[] { 0X21, 0XDE };
            byte[] Recbuf = new byte[1];
            int Recsize = 1;  //接收的数据长度
            Debug.WriteLine("发送Go Command命令 \r\n跳转地址为:0X" + addr.ToString("X8"));
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, (UInt32)mDataBuff.LongLength, "发送Go Command命令 \r\n跳转地址为:0X" + addr.ToString("X8"));
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
            {
                if (Recbuf[0] == MCU_ACK)
                {
                    Sendbuf = new byte[5];
                    Sendbuf[0] = (byte)((addr & 0xff000000) >> 24);
                    Sendbuf[1] = (byte)((addr & 0x00ff0000) >> 16);
                    Sendbuf[2] = (byte)((addr & 0x0000ff00) >> 8);
                    Sendbuf[3] = (byte)(addr & 0x000000ff);
                    byte[] Data = new byte[4];  //用于计算异或值
                    Array.Copy(Sendbuf, Data, 4);
                    Sendbuf[4] = Return_Xor(Data);  //得到异或校验值
                    if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize)) //发送地址
                    {
                        if (Recbuf[0] == MCU_ACK)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 下载Flash数据并执行跳转  返回0成功  其它错误信息
        /// </summary>
        /// <returns>0成功 1写数据命令错误 2度数据命令错误  3写入与读出不相同  </returns>
        private int DownLoadFlashDataAndGo()
        {
            //开始下载升级文件
            UInt32 SendSize = 0; //当前发送的数据长度
            UInt32 Upwaddr = mFlashAddr; //当前写地址
            UInt32 Upfsize = (UInt32)mDataBuff.LongLength; //得到文件大小
            UInt32 Upfsizeoff = 0; //文件地址偏移

            while (Upfsize > Upfsizeoff)
            {
                //当前写数据缓存大小
                SendSize = ((Upfsize - Upfsizeoff) >= DownBlockSize) ? DownBlockSize : (Upfsize - Upfsizeoff);
                byte[] Sendbuff = new byte[SendSize]; //发送缓存
                byte[] Recbuff = new byte[SendSize];//接收缓存
                Array.Copy(mDataBuff, Upfsizeoff, Sendbuff, 0, SendSize); //拷贝数据
                //写入数据
                if (Write_Memory_command(Upwaddr, Sendbuff) != true)
                {
                    return 1;
                }
                //读出刚才写入的数据
                if (Read_Memory_command(Upwaddr, Recbuff) != true)
                {
                    return 2;
                }
                //比较写入与读出的数据是否相同
                string SendStr = System.Text.Encoding.Default.GetString(Sendbuff);
                string RecStr = System.Text.Encoding.Default.GetString(Recbuff);
                if (string.CompareOrdinal(SendStr, RecStr) != 0)
                {
                    return 3;
                }

                Upwaddr += SendSize;//写地址偏移
                Upfsizeoff += SendSize; //文件读写指针偏移
                OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, Upfsizeoff, ""); //触发变更事件
            }

            if (Go_command(mFlashAddr) == false)  //跳转
            {
                return 4;
            }
            return 0;
        }

        /// <summary>
        /// 写保护  Secbuf 扇区号缓存  number 扇区数
        /// </summary>
        /// <param name="Secbuf"></param>
        /// <returns></returns>
        public bool Write_Protect_command(byte[] Secbuf)
        {
            byte[] Sendbuf = new byte[] { 0X63, 0X9C };
            byte[] Recbuf = new byte[1];
            int Recsize = 1;  //接收的数据长度
            Debug.WriteLine("发送Write Protect Command命令");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Write Protect Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
            {
                if (Recbuf[0] == MCU_ACK)
                {
                    Sendbuf = new byte[Secbuf.Length + 2];
                    Sendbuf[0] = (byte)(Secbuf.Length - 1);//扇区数
                    Array.Copy(Secbuf, 0, Sendbuf, 1, Secbuf.Length); //拷贝扇区缓存 扇区编号
                    byte[] data = new byte[Sendbuf.Length - 1];  //用于计算校验值
                    Array.Copy(Sendbuf, data, data.Length);
                    Sendbuf[Sendbuf.Length - 2] = Return_Xor(data); //校验值
                    if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
                    {
                        if (Recbuf[0] == MCU_ACK)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }


        /// <summary>
        /// 去除写保护
        /// </summary>
        /// <returns></returns>
        public bool Write_Unprotect_command()
        {
            byte[] Sendbuf = new byte[] { 0X73, 0X8C };
            byte[] Recbuf = new byte[1];
            int Recsize = 2;  //接收的数据长度
            Debug.WriteLine("发送Write Unprotect Command命令");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Write Unprotect Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
            {
                if ((Recbuf[0] == MCU_ACK) && (Recbuf[1] == MCU_ACK))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 读保护
        /// </summary>
        /// <returns></returns>
        bool Readout_Protect_command()
        {
            byte[] Sendbuf = new byte[] { 0X82, 0X7D };
            byte[] Recbuf = new byte[1];
            int Recsize = 2;  //接收的数据长度
            Debug.WriteLine("发送Readout Protect Command命令");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Readout Protect Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize))  //发送命令
            {
                if ((Recbuf[0] == MCU_ACK) && (Recbuf[1] == MCU_ACK))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 去除读保护
        /// </summary>
        /// <returns></returns>
        bool Readout_Unprotect_command()
        {
            byte[] Sendbuf = new byte[] { 0X92, 0X6D };
            byte[] Recbuf = new byte[1];
            int Recsize = 2;  //接收的数据长度
            Debug.WriteLine("发送Readout Unprotect Command命令");
            OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "发送Readout Unprotect Command命令");
            if (Usart_SendCmmRec(Sendbuf, ref Recbuf, Recsize, 60 * 1000))  //发送命令
            {
                if ((Recbuf[0] == MCU_ACK) && (Recbuf[1] == MCU_ACK))
                {
                    OnDownChangeEventHandler((UInt32)mDataBuff.LongLength, 0, "去除读保护成功");
                    return true;
                }
            }
            return false;
        }

        public static string ToHexString(byte[] bytes) // 0xae00cf => "AE00CF "  
        {
            string hexString = string.Empty;
            if (bytes != null)
            {
                StringBuilder strB = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    strB.Append(bytes[i].ToString("X2") + " ");

                }
                hexString = strB.ToString();
            }
            return hexString;
        }
    }
}
