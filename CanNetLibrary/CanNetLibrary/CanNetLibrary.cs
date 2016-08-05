using System;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading;
using Ixxat.Vci3;
using Ixxat.Vci3.Bal;
using Ixxat.Vci3.Bal.Can;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
///  Область имен, в которой находятся классы для работы с CAN-адаптерами
/// </summary>
namespace CanNetLibrary
{
    /// <summary>
    /// Делегат события canmsg
    /// </summary>
    public delegate void MethodContainer(CanMsg cmsg);

    /// <summary>
    /// Класс CAN-сообщения
    /// </summary>
    public class CanMsg
    {
        /// <summary> 
        /// Сообщение
        /// </summary>
        private byte[] msg;

        /// <summary>
        /// Байты данных
        /// </summary>
        public byte this[byte index]   //Перечислитель для редактирования содержимого массива сообщения 
        {
            get { return msg[index]; }

            set { msg[index] = value; }
        }

        /// <summary>
        /// Размер сообщения
        /// </summary>
        public byte Size
        {
            get { return Size; }
            private set { Size = value; }
        }

        /// <summary>
        /// Флаг расширенного идентификатора сообщения
        /// </summary>
        bool Extended;

        /// <summary> 
        /// Уникальный идентификатор сообщения
        /// </summary>
        public UInt32 Id
        {
            get { return Id; }
            private set { Id = value; }
        }

        /// <summary>
        /// Дополнительная строка-пояснения к can-сообщению.(удобно для вывода в консоль)
        /// </summary>
        public string comment
        {
            get { return comment; }
            set { comment = value; }
        }

        public CanMsg(UInt32 id, bool Extended, byte Size) // id, exnt, size, remote
        {
            
            this.Extended = Extended;
            this.Size = Size;
            if (Size <= 8)
            {
                msg = new byte[Size];
            }
            else
                throw new System.ArgumentException("Неправильный входной параметр аргумента", "original");
            
            if (Extended == false)
            {
                if (id <= 0x7FF)
                    this.Id = id;
                else
                    throw new System.ArgumentException("Неправильный входной параметр аргумента", "original");
            }
            if (Extended == true)
            {
                if (id <= 0x1FFFFFFF)
                    this.Id = id;
                else
                    throw new System.ArgumentException("Неправильный входной параметр аргумента", "original");
            }

            
            
        }

        public static CanMsg CreateRemote(UInt32 id0, bool Extended0)
        {
            CanMsg CreateRemote =  new CanMsg(id0, Extended0, 0);

            CreateRemote.Extended = Extended0;
            CreateRemote.Size = 0;

            if (CreateRemote.Extended == false)
            {
                if (id0 <= 0x7FF)
                    CreateRemote.Id = id0;
                else
                    throw new System.ArgumentException("Неправильный входной параметр аргумента", "original");
            }
            if (CreateRemote.Extended == true)
            {
                if (id0 <= 0x1FFFFFFF)
                    CreateRemote.Id = id0;
                else
                    throw new System.ArgumentException("Неправильный входной параметр аргумента", "original");
            }

            return CreateRemote;
        }

    }

    /// <summary>
    ///  Интерфейс для работы с CAN-адаптерами
    /// </summary>
    public interface CanServer
    {
        string[] ListOfDevices();
        bool StartDevice(int adapterNo);
        bool StopDevice();
        void TransmitData(CanMsg msg);
        CanMsg ReceiveThreadFunc();
        void SetSpeed(int Speed);
        event MethodContainer CanMessageRecieved;
    }

    /// <summary>
    ///  Класс для работы с VCI CAN
    /// </summary>
    public class VciCan : CanServer
    {
        IVciDevice mDevice;
        IVciDeviceList deviceList;
        ICanControl mCanCtl;
        ICanChannel mCanChn;
        ICanMessageWriter mWriter;
        ICanMessageReader mReader;
        AutoResetEvent mRxEvent;
        CanBitrate vCanSpeed = CanBitrate.Cia250KBit;
 
        public event MethodContainer CanMessageRecieved;


        /// <summary>
        ///  Проверка списка CAN-адаптеров
        /// </summary>
        public string[] ListOfDevices()
        {
            string CheckDevice = "";
            IVciDeviceManager deviceManager = null;
            System.Collections.IEnumerator deviceEnum = null;

            try
            {
                deviceManager = VciServer.GetDeviceManager();
                deviceList = deviceManager.GetDeviceList();
                deviceEnum = deviceList.GetEnumerator();
                deviceEnum.MoveNext();
                do
                {
                    mDevice = deviceEnum.Current as IVciDevice;
                    object serialNumberGuid = mDevice.UniqueHardwareId;
                    string serialNumberText = GetSerialNumberText(ref serialNumberGuid);
                    CheckDevice += mDevice.Description + " " + mDevice.UniqueHardwareId.ToString() + ";";
                } while (deviceEnum.MoveNext() != false);



            }
            catch (Exception)
            {

                CheckDevice = "Error: No VCI device installed";

            }
            finally
            {
                DisposeVciObject(deviceManager);
                DisposeVciObject(deviceEnum);
            }
            return CheckDevice.Split(';');
        }

        /// <summary>
        ///  Инициализация работы CAN-адаптера
        /// </summary>
        public bool StartDevice(int adapterNo)
        {
            byte canNo = 0;
            IBalObject bal = null;
            System.Collections.IEnumerator deviceEnum = null;
            int i = -1;
            try
            {

                deviceEnum = deviceList.GetEnumerator();
                deviceEnum.MoveNext();

                do
                {
                    i++;
                    if (i == adapterNo)
                        mDevice = deviceEnum.Current as IVciDevice;
                } while (deviceEnum.MoveNext() != false);

                bal = mDevice.OpenBusAccessLayer();
                mCanChn = bal.OpenSocket(canNo, typeof(ICanChannel)) as ICanChannel;
                mCanChn.Initialize(1024, 128, false);
                mReader = mCanChn.GetMessageReader();
                mReader.Threshold = 1;
                mRxEvent = new AutoResetEvent(false);
                mReader.AssignEvent(mRxEvent);
                mWriter = mCanChn.GetMessageWriter();
                mWriter.Threshold = 1;
                mCanChn.Activate();
                int a = bal.Resources.Count - 1;
                mCanCtl = bal.OpenSocket(canNo, typeof(ICanControl)) as ICanControl;
                mCanCtl.InitLine(CanOperatingModes.Standard | CanOperatingModes.ErrFrame
                                , vCanSpeed);
                mCanCtl.SetAccFilter(CanFilter.Std,
                                     (uint)CanAccCode.All, (uint)CanAccMask.All);
                mCanCtl.StartLine();

                return true;
            }
            catch (Exception)
            {

                return false;
            }
            finally
            {
                DisposeVciObject(bal);
                DisposeVciObject(deviceEnum);

            }
        }

        /// <summary>
        ///  Остановка работы CAN-адаптера
        /// </summary>
        public bool StopDevice()
        {
            try
            {
                mCanCtl.StopLine();
                return true;
            }
            catch (Exception)
            {

                return false;
            }
            finally
            {
                DisposeVciObject(mCanCtl);
                DisposeVciObject(mDevice);
            }
        }

        /// <summary>
        ///  Отправка CAN сообщения
        /// </summary>
        public void TransmitData(CanMsg msg)
        {
            CanMessage canMsg = new CanMessage();
            // ExtendedFrameFormat - тип идентификатора - 11 или 22 бит
            

            canMsg.TimeStamp = 0;
            //canMsg.Identifier = 0x100;
            canMsg.Identifier = msg.Id;
            canMsg.FrameType = CanMsgFrameType.Data;        
            canMsg.DataLength = msg.Size;
            if (msg.Size == 0)
                canMsg.RemoteTransmissionRequest = true;
            canMsg.SelfReceptionRequest = true;  // Отображение сообщения в консоле

            for (Byte i = 0; i < canMsg.DataLength; i++)
            {
                canMsg[i] = msg.Size;
            }

            // Запись сообщения в FIFO(буфер can-адаптера?)
            mWriter.SendMessage(canMsg);
        }

        /// <summary>
        ///  Поток проверки CAN-буфера
        /// </summary>
        public CanMsg ReceiveThreadFunc()
        {
            (new System.Threading.Thread(delegate() {
            string ReceiveThreadFunc = "";
            CanMessage canMessage;
            CanMsg cmsg = null;
             do
              {
            // Принять кан сообщение из буфера адаптера
            while (mReader.ReadMessage(out canMessage))
            {
                cmsg = new CanMsg(canMessage.Identifier, canMessage.ExtendedFrameFormat, canMessage.DataLength);
                switch (canMessage.FrameType)
                {
                    //
                    //  действия для каждого типа данных
                    //
                        
                    case CanMsgFrameType.Data:
                        {
                            if (!canMessage.RemoteTransmissionRequest)
                            {

                                ReceiveThreadFunc = "\nTime: " + canMessage.TimeStamp + "  ID: " + canMessage.Identifier + "  DLC: " + canMessage.DataLength + " Data: ";
                                cmsg.comment = "\nTime: " + canMessage.TimeStamp + "  ID: " + canMessage.Identifier + "  DLC: " + canMessage.DataLength + " Data: ";

                                for (int index = 0; index < canMessage.DataLength; index++)
                                {
                                    ReceiveThreadFunc += canMessage[index] + " ";
                                    cmsg.comment += canMessage[index] + " ";
                                    cmsg[(byte)index] = Convert.ToByte(canMessage[index]);
                                }
                            }
                            else
                            {
                                ReceiveThreadFunc = "\nTime: " + canMessage.TimeStamp + "  ID: " + canMessage.Identifier + "  DLC: " + canMessage.DataLength + " Remote Frame ";
                                cmsg.comment += "\nTime: " + canMessage.TimeStamp + "  ID: " + canMessage.Identifier + "  DLC: " + canMessage.DataLength + " Remote Frame ";
                            }
                            break;
                        }

                    //
                    // Информационные сообщения
                    //
                    case CanMsgFrameType.Info:
                        {
                            switch ((CanMsgInfoValue)canMessage[0])
                            {
                                case CanMsgInfoValue.Start:

                                 //   ReceiveThreadFunc = "\nCAN started...";
                                    cmsg.comment = "\nCAN started...";
                                    break;
                                case CanMsgInfoValue.Stop:
                                 //   cmsg.comment = "\nCAN stopped...";
                                    cmsg.comment = "\nCAN stopped...";
                                    break;
                                case CanMsgInfoValue.Reset:
                                //    cmsg.comment = "\nCAN reseted...";
                                    cmsg.comment = "\nCAN reseted...";
                                    break;
                            }
                            break;
                        }

                    //
                    // show error frames
                    //
                    case CanMsgFrameType.Error:
                        {
                            switch ((CanMsgError)canMessage[0])
                            {
                                case CanMsgError.Stuff:
                                    cmsg.comment = "\nstuff error...";
                                    break;
                                case CanMsgError.Form:
                                    cmsg.comment = "\nform error...";
                                    break;
                                case CanMsgError.Acknowledge:
                                    cmsg.comment = "\nacknowledgment error...";
                                    break;
                                case CanMsgError.Bit:
                                    cmsg.comment = "\nbit error...";
                                    break;
                                case CanMsgError.Crc:
                                    cmsg.comment = "\nCRC error...";
                                    break;
                                case CanMsgError.Other:
                                    cmsg.comment = "\nother error...";
                                    break;
                            }
                            break;
                        }
                }
            }
            CanMessageRecieved(cmsg);
           } while (0 == 0);
            // return cmsg;
            })).Start();
            return null;
        }

        /// <summary>
        ///  Функция реализующая высвобождение неуправляемых объектов
        /// </summary>
        void DisposeVciObject(object obj)
        {
            if (null != obj)
            {
                IDisposable dispose = obj as IDisposable;
                if (null != dispose)
                {
                    dispose.Dispose();
                    obj = null;
                }
            }
        }

        /// <summary>
        ///  Вывод серийного номера VCI CAN-адаптера
        /// </summary>
        string GetSerialNumberText(ref object serialNumberGuid)
        {
            string resultText;


            if (serialNumberGuid.GetType() == typeof(System.Guid))
            {
                System.Guid tempGuid = (System.Guid)serialNumberGuid;
                byte[] byteArray = tempGuid.ToByteArray();
                if (((char)byteArray[0] == 'H') && ((char)byteArray[1] == 'W'))
                {
                    resultText = "";
                    int i = 0;
                    while (true)
                    {
                        if (byteArray[i] != 0)
                            resultText += (char)byteArray[i];
                        else
                            break;
                        i++;

                        if (i == byteArray.Length)
                            break;
                    }
                }
                else
                {
                    resultText = serialNumberGuid.ToString();
                }
            }
            else
            {
                string tempString = (string)(string)serialNumberGuid;
                resultText = "";
                for (int i = 0; i < tempString.Length; i++)
                {
                    if (tempString[i] != 0)
                        resultText += tempString[i];
                    else
                        break;
                }
            }

            return resultText;
        }

        /// <summary>
        ///  Назначить скорость передачи данных CAN-адаптера
        /// </summary>
        public void SetSpeed(int Speed)
        {
            switch (Speed)
            {
                case 250: vCanSpeed = CanBitrate.Cia250KBit; break;
                case 1000: vCanSpeed = CanBitrate.Cia1000KBit; break;
                case 500: vCanSpeed = CanBitrate.Cia500KBit; break;
                case 800: vCanSpeed = CanBitrate.Cia800KBit; break;
                case 125: vCanSpeed = CanBitrate.Cia125KBit; break;
                case 10: vCanSpeed = CanBitrate.Cia10KBit; break;
                case 20: vCanSpeed = CanBitrate.Cia20KBit; break;
                case 80: vCanSpeed = CanBitrate.Cia800KBit; break;

            }

        }


    }

}
