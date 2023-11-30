using System;
using System.Collections.Generic;
using System.Text;

namespace CommPort
{
    /// <summary>
    /// 设备管理器中COM口封装实体类,用于在ComBox中显示
    /// </summary>
    public class DevicePort : IComparable
    {
        public DevicePort(String portName)
        {
            this.portName = portName;
        }


        public DevicePort(String displayName, String portName)
        {
            this.displayName = displayName;
            this.portName = portName;
        }
        /// <summary>
        /// 显示的名字
        /// </summary>
        String displayName;

        public String DisplayName
        {
            get { return displayName; }
            set { displayName = value; }
        }

        /// <summary>
        /// 实际串口名
        /// </summary>
        String portName;

        public String PortName
        {
            get { return portName; }
            set { portName = value; }
        }

        public int CompareTo(object obj)
        {
            DevicePort port = obj as DevicePort;
            return this.displayName.CompareTo(port.DisplayName);
        }

        // override object.Equals
        public override bool Equals(object obj)
        {
            DevicePort port = obj as DevicePort;
            if (port == null)
            {
                return false;
            }
            return this.portName.Equals(port.PortName);
        }
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

    }
}
