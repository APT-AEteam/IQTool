using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
using System.Management;

namespace CommPort
{
    class EnumComPort
    {

        /// <summary>
        /// 获取两个串口中不同的串口名
        /// </summary>
        /// <param name="devicePorts"></param>
        /// <param name="seriaPorts"></param>
        /// <returns></returns>
        public static List<DevicePort> GetSpecialPortNames(string SpecialComFlag1,string SpecialComFlag2)
        {
            List<DevicePort> list = new List<DevicePort>();

            ManagementObjectSearcher searcher = null;  //它用于调用有关管理信息的指定查询
            ManagementObjectCollection mbc = null;  //表示通过 WMI 检索到的管理对象的不同集合
            try
            {
                searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name like '通讯端口%(COM%' or Name like '%USB%(COM%' ");
                mbc = searcher.Get();
                foreach (ManagementObject mgt in mbc)
                {
                    PropertyDataCollection properties = mgt.Properties;
                    String name = Convert.ToString(mgt["Name"]);
                    int start = name.IndexOf("(");
                    int length = name.LastIndexOf(")") - start - 1;

                    if (start == -1 || length <= 0)
                    {
                        //LOG.Error(String.Format("获取COM口错误,跳过此COM:name==>{0},start==>{1},length==>{2}", name, start, length));
                        continue;
                    }

                    list.Add(new DevicePort(name, name.Substring(start + 1, length)));
                    mgt.Dispose();
                }

            }
            catch (Exception)
            {
                //LOG.Error("读取设务管理器串口报错:" + ex.StackTrace);
            }
            finally
            {
                if (searcher != null)
                {
                    searcher.Dispose();
                }
                if (mbc != null)
                {
                    mbc.Dispose();
                }
            }
            list.Sort();

            List<DevicePort> listWillRet = new List<DevicePort>();
            foreach (DevicePort port in list)
            {
                if (port.DisplayName.Contains(SpecialComFlag1))
                {
                    listWillRet.Add(new DevicePort(port.DisplayName, port.PortName));
                }
                else if(port.DisplayName.Contains(SpecialComFlag2))
                {
                    listWillRet.Add(new DevicePort(port.DisplayName, port.PortName));
                }
            }
            return listWillRet;
        }

        public static List<DevicePort> GetPortNames()
        {
            List<DevicePort> list = new List<DevicePort>();

            ManagementObjectSearcher searcher = null;
            ManagementObjectCollection mbc = null;
            try
            {

                searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name like '通讯端口%(COM%' or Name like '%USB%(COM%' ");
                mbc = searcher.Get();
                foreach (ManagementObject mgt in mbc)
                {
                    PropertyDataCollection properties = mgt.Properties;
                    String name = Convert.ToString(mgt["Name"]);
                    int start = name.IndexOf("(");
                    int length = name.LastIndexOf(")") - start - 1;

                    if (start == -1 || length <= 0)
                    {
                        //LOG.Error(String.Format("获取COM口错误,跳过此COM:name==>{0},start==>{1},length==>{2}", name, start, length));
                        continue;
                    }

                    list.Add(new DevicePort(name, name.Substring(start + 1, length)));
                    mgt.Dispose();
                }

            }
            catch (Exception)
            {
                //LOG.Error("读取设务管理器串口报错:" + ex.StackTrace);
            }
            finally
            {
                if (searcher != null)
                {
                    searcher.Dispose();
                }
                if (mbc != null)
                {
                    mbc.Dispose();
                }
            }
            list.Sort();
            return list;
        }
    }
}
