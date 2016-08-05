using System;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using Ixxat.Vci3;
using Ixxat.Vci3.Bal;
using Ixxat.Vci3.Bal.Can;
using System.Collections.Generic;
using System.Runtime.InteropServices;


namespace CanConNet
{
    class CanServer
    {
        /// <summary>
        ///  enum выбора CAN
        /// </summary>
        enum CanInterface {None, VCI, NotVCI};

        /// <summary>
        ///  Сигнализатор CAN интерфейса
        /// </summary>
       static CanInterface CurCan = new CanInterface();

        /// <summary>
        ///  Интерфейс для работы с VCI CAN
        /// </summary>
       public class vcican
        {
            public static bool IsOpen = false;
            static IVciDevice mDevice;
            static IVciDeviceList deviceList;
            static ICanControl mCanCtl;
            static ICanChannel mCanChn;
            static ICanMessageWriter mWriter;
            static ICanMessageReader mReader;
            static AutoResetEvent mRxEvent;

            public static string CheckDevice()
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
                return CheckDevice;
            }

            public static bool InitDevice(byte canNo, int canSpeed, int adapterNo)
            {
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
                                    , CanBitrate.Cia250KBit);
                    mCanCtl.SetAccFilter(CanFilter.Std,
                                         (uint)CanAccCode.All, (uint)CanAccMask.All);
                    mCanCtl.StartLine();

                    return true;
                }
                catch (Exception e)
                {
                    MessageBox.Show("Error: Initializing socket failed: " + e.Message);
                    return false;
                }
                finally
                {
                    DisposeVciObject(bal);
                    DisposeVciObject(deviceEnum);
                    IsOpen = true;
                }
            }

            public static bool StopDevice()
            {
                try
                {
                    mCanCtl.StopLine();
                    return true;
                }
                catch (Exception e)
                {
                    MessageBox.Show("Error: Initializing socket failed: " + e.Message);
                    return false;
                }
                finally
                {
                    DisposeVciObject(mCanCtl);
                    DisposeVciObject(mDevice);
                    IsOpen = false;
                }
            }

            /// <summary>
            ///  Отправка CAN сообщения
            /// </summary>
            public static void TransmitData(byte[] msg)
            {
                CanMessage canMsg = new CanMessage();
                // ExtendedFrameFormat - тип идентификатора - 11 или 22 бит
                canMsg.TimeStamp = 0;
                canMsg.Identifier = 0x100;
                canMsg.FrameType = CanMsgFrameType.Data;
                canMsg.DataLength = 8;
                canMsg.SelfReceptionRequest = true;  // Отображение сообщения в консоле

                for (Byte i = 0; i < canMsg.DataLength; i++)
                {
                    canMsg[i] = msg[i];
                }

                // Запись сообщения в FIFO(буфер can-адаптера?)
                mWriter.SendMessage(canMsg);
            }


            public static string ReceiveThreadFunc()
            {
                string ReceiveThreadFunc = "";
                CanMessage canMessage;
                
                    // Принять кан сообщение из буфера адаптера
                    while (mReader.ReadMessage(out canMessage))
                    {
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

                                        for (int index = 0; index < canMessage.DataLength; index++)
                                        {
                                            ReceiveThreadFunc += canMessage[index] + " ";

                                        }
                                    }
                                    else
                                    {
                                        ReceiveThreadFunc = "\nTime: " + canMessage.TimeStamp + "  ID: " + canMessage.Identifier + "  DLC: " + canMessage.DataLength + " Remote Frame ";
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

                                            ReceiveThreadFunc = "\nCAN started...";
                                            break;
                                        case CanMsgInfoValue.Stop:
                                            ReceiveThreadFunc = "\nCAN stopped...";
                                            break;
                                        case CanMsgInfoValue.Reset:
                                            ReceiveThreadFunc = "\nCAN reseted...";
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
                                            ReceiveThreadFunc = "\nstuff error...";
                                            break;
                                        case CanMsgError.Form:
                                            ReceiveThreadFunc = "\nform error...";
                                            break;
                                        case CanMsgError.Acknowledge:
                                            ReceiveThreadFunc = "\nacknowledgment error...";
                                            break;
                                        case CanMsgError.Bit:
                                            ReceiveThreadFunc = "\nbit error...";
                                            break;
                                        case CanMsgError.Crc:
                                            ReceiveThreadFunc = "\nCRC error...";
                                            break;
                                        case CanMsgError.Other:
                                            ReceiveThreadFunc = "\nother error...";
                                            break;
                                    }
                                    break;
                                }
                        }
                    }
                
                return ReceiveThreadFunc;
            }


            static void DisposeVciObject(object obj)
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

            static string GetSerialNumberText(ref object serialNumberGuid)
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




        }

       public class NotVCI
       {

       }

       static string CheckDevice()
       {
           string CheckDevice = "";
           
           return CheckDevice;
       }
    }
}
