//////////////////////////////////////////////////////////////////////////
// IXXAT Automation GmbH
//////////////////////////////////////////////////////////////////////////
/**
  Demo application for the IXXAT VCI .NET-API.

  @file "CanConNet.cs"

  @note 
    This demo demonstrates the following VCI features
    - adapter selection
    - controller initialization 
    - creation of a message channel
    - transmission / reception of CAN messages
*/
//////////////////////////////////////////////////////////////////////////
// (C) 2002-2011 IXXAT Automation GmbH, all rights reserved
//////////////////////////////////////////////////////////////////////////

/*****************************************************************************
 * used namespaces
 ****************************************************************************/
using System;
using System.Text;
using System.Collections;
using System.Threading;
using Ixxat.Vci3;
using Ixxat.Vci3.Bal;
using Ixxat.Vci3.Bal.Can;


/*****************************************************************************
 * namespace CanConNet
 ****************************************************************************/
namespace CanConNet
{
  //##########################################################################
  /// <summary>
  ///   This class provides the entry point for the IXXAT VCI .NET 2.0 API
  ///   demo application. 
  /// </summary>
  //##########################################################################
  class CanConNet
  {
    #region Member variables

    /// <summary>
    ///   Reference to the used VCI device.
    /// </summary>
    static IVciDevice mDevice;

    /// <summary>
    ///   Reference to the CAN controller.
    /// </summary>
    static ICanControl mCanCtl;

    /// <summary>
    ///   Reference to the CAN message communication channel.
    /// </summary>
    static ICanChannel mCanChn;

    /// <summary>
    ///   Reference to the message writer of the CAN message channel.
    /// </summary>
    static ICanMessageWriter mWriter;

    /// <summary>
    ///   Reference to the message reader of the CAN message channel.
    /// </summary>
    static ICanMessageReader mReader;

    /// <summary>
    ///   Thread that handles the message reception.
    /// </summary>
    static Thread rxThread;

    /// <summary>
    ///   Quit flag for the receive thread.
    /// </summary>
    static long mMustQuit = 0;

    /// <summary>
    ///   Event that's set if at least one message was received.
    /// </summary>
    static AutoResetEvent mRxEvent;

    #endregion

    #region Application entry point

    //************************************************************************
    /// <summary>
    ///   The entry point of this console application.
    /// </summary>
    //************************************************************************
    static void Main(string[] args)
    {
      Console.WriteLine(" >>>> VCI - .NET 2.0 - API Example V1.0 <<<<");
      Console.WriteLine(" initializes the CAN with 125 kBaud");
      Console.WriteLine(" key 't' sends a messageID 100H");
      Console.WriteLine(" shows all received messages");
      Console.WriteLine(" Quit the application with ESC\n");

      Console.Write(" Select Adapter...\r");
      SelectDevice();
      Console.WriteLine(" Select Adapter.......... OK !\n");

      Console.Write(" Initialize CAN...\r");

      if (!InitSocket(0))
      {
        Console.WriteLine(" Initialize CAN............ FAILED !\n");
      }
      else
      {
        Console.WriteLine(" Initialize CAN............ OK !\n");

        //
        // start the receive thread
        //
        rxThread = new Thread(new ThreadStart(ReceiveThreadFunc));
        rxThread.Start();

        //
        // wait for keyboard hit transmit  CAN-Messages cyclically
        //
        ConsoleKeyInfo cki = new ConsoleKeyInfo();

        do
        {
          while (Console.KeyAvailable)
          {
            Thread.Sleep(10);
          }
          cki = Console.ReadKey(true);
          if (cki.Key == ConsoleKey.T)
          {
            TransmitData();
          }
        } while (cki.Key != ConsoleKey.Escape);

        //
        // tell receive thread to quit
        //
        Interlocked.Exchange(ref mMustQuit, 1);

        //
        // Wait for termination of receive thread
        //
        rxThread.Join();
      }

      Console.Write("\n Free VCI - Resources...\r");
      FinalizeApp();
      Console.WriteLine(" Free VCI - Resources........ OK !\n");
    }

    #endregion

    #region Device selection

    //************************************************************************
    /// <summary>
    ///   Selects the first CAN adapter.
    /// </summary>
    //************************************************************************
    static void SelectDevice()
    {
      IVciDeviceManager deviceManager = null;
      IVciDeviceList    deviceList    = null;
      IEnumerator       deviceEnum    = null;

      try
      {
        //
        // Get device manager from VCI server
        //
        deviceManager = VciServer.GetDeviceManager();

        //
        // Get the list of installed VCI devices
        //
        deviceList = deviceManager.GetDeviceList();
        deviceList = deviceManager.GetDeviceList();

        //
        // Get enumerator for the list of devices
        //
        deviceEnum = deviceList.GetEnumerator();

        //
        // Get first device
        //
        deviceEnum.MoveNext();
        mDevice = deviceEnum.Current as IVciDevice;

        // show the device name and serial number
        object serialNumberGuid = mDevice.UniqueHardwareId;
        string serialNumberText = GetSerialNumberText(ref serialNumberGuid);
        Console.Write(" Interface    : " + mDevice.Description + "\n");
        Console.Write(" Serial number: " + serialNumberText + "\n");
      }
      catch (Exception)
      {
        Console.WriteLine("Error: No VCI device installed");
      }
      finally
      {
        //
        // Dispose device manager ; it's no longer needed.
        //
        DisposeVciObject(deviceManager);

        //
        // Dispose device list ; it's no longer needed.
        //
        DisposeVciObject(deviceList);

        //
        // Dispose device list ; it's no longer needed.
        //
        DisposeVciObject(deviceEnum);
      }
    }

    #endregion

    #region Opening socket

    //************************************************************************
    /// <summary>
    ///   Opens the specified socket, creates a message channel, initializes
    ///   and starts the CAN controller.
    /// </summary>
    /// <param name="canNo">
    ///   Number of the CAN controller to open.
    /// </param>
    /// <returns>
    ///   A value indicating if the socket initialization succeeded or failed.
    /// </returns>
    //************************************************************************
    static bool InitSocket(Byte canNo)
    {
      IBalObject bal = null;
      bool succeeded = false;

      try
      {
        //
        // Open bus access layer
        //
        bal = mDevice.OpenBusAccessLayer();

        //
        // Open a message channel for the CAN controller
        //
        mCanChn = bal.OpenSocket(canNo, typeof(ICanChannel)) as ICanChannel;

        // Initialize the message channel
        mCanChn.Initialize(1024, 128, false);

        // Get a message reader object
        mReader = mCanChn.GetMessageReader();

        // Initialize message reader
        mReader.Threshold = 1;

        // Create and assign the event that's set if at least one message
        // was received.
        mRxEvent = new AutoResetEvent(false);
        mReader.AssignEvent(mRxEvent);

        // Get a message wrtier object
        mWriter = mCanChn.GetMessageWriter();

        // Initialize message writer
        mWriter.Threshold = 1;

        // Activate the message channel
        mCanChn.Activate();


        //
        // Open the CAN controller
        //
        mCanCtl = bal.OpenSocket(canNo, typeof(ICanControl)) as ICanControl;

        // Initialize the CAN controller
        mCanCtl.InitLine( CanOperatingModes.Standard | CanOperatingModes.ErrFrame
                        , CanBitrate.Cia125KBit);

        // Set the acceptance filter
        mCanCtl.SetAccFilter(CanFilter.Std, 
                             (uint)CanAccCode.All, (uint)CanAccMask.All);

        // Start the CAN controller
        mCanCtl.StartLine();

        succeeded = true;
      }
      catch (Exception)
      {
        Console.WriteLine("Error: Initializing socket failed");
      }
      finally
      {
        //
        // Dispose bus access layer
        //
        DisposeVciObject(bal);
      }

      return succeeded;
    }

    #endregion

    #region Message transmission

    /// <summary>
    ///   Transmits a CAN message with ID 0x100.
    /// </summary>
    static void TransmitData()
    {
      CanMessage canMsg = new CanMessage();

      canMsg.TimeStamp  = 0;
      canMsg.Identifier = 0x100;
      canMsg.FrameType  = CanMsgFrameType.Data;
      canMsg.DataLength = 8;
      canMsg.SelfReceptionRequest = true;  // show this message in the console window

      for (Byte i = 0; i < canMsg.DataLength; i++)
      {
        canMsg[i] = i;
      }

      // Write the CAN message into the transmit FIFO
      mWriter.SendMessage(canMsg);
    }

    #endregion

    #region Message reception

    //************************************************************************
    /// <summary>
    ///   This method is the works as receive thread.
    /// </summary>
    //************************************************************************
    static void ReceiveThreadFunc()
    {
      CanMessage canMessage;

      do
      {
        // Wait 100 msec for a message reception
        if (mRxEvent.WaitOne(100, false))
        {
          // read a CAN message from the receive FIFO
          while (mReader.ReadMessage(out canMessage))
          {
            switch (canMessage.FrameType)
            {
              //
              // show data frames
              //
              case CanMsgFrameType.Data:
                {
                  if (!canMessage.RemoteTransmissionRequest)
                  {
                    Console.Write("\nTime: {0,10}  ID: {1,3:X}  DLC: {2,1}  Data:",
                                  canMessage.TimeStamp,
                                  canMessage.Identifier,
                                  canMessage.DataLength);

                    for (int index = 0; index < canMessage.DataLength; index++)
                    {
                      Console.Write(" {0,2:X}", canMessage[index]);
                    }
                  }
                  else
                  {
                    Console.Write("\nTime: {0,10}  ID: {1,3:X}  DLC: {2,1}  Remote Frame",
                                  canMessage.TimeStamp,
                                  canMessage.Identifier,
                                  canMessage.DataLength);
                  }
                  break;
                }

              //
              // show informational frames
              //
              case CanMsgFrameType.Info:
                {
                  switch ((CanMsgInfoValue)canMessage[0])
                  {
                    case CanMsgInfoValue.Start:
                      Console.Write("\nCAN started...");
                      break;
                    case CanMsgInfoValue.Stop:
                      Console.Write("\nCAN stopped...");
                      break;
                    case CanMsgInfoValue.Reset:
                      Console.Write("\nCAN reseted...");
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
                      Console.Write("\nstuff error...");
                      break;
                    case CanMsgError.Form:
                      Console.Write("\nform error...");
                      break;
                    case CanMsgError.Acknowledge:
                      Console.Write("\nacknowledgment error...");
                      break;
                    case CanMsgError.Bit:
                      Console.Write("\nbit error...");
                      break;
                    case CanMsgError.Crc:
                      Console.Write("\nCRC error...");
                      break;
                    case CanMsgError.Other:
                      Console.Write("\nother error...");
                      break;
                  }
                  break;
                }
            }
          }
        }
      } while (0 == mMustQuit);
    }

    #endregion

    #region Utility methods

    /// <summary>
    /// Returns the UniqueHardwareID GUID number as string which
    /// shows the serial number.
    /// Note: This function will be obsolete in later version of the VCI.
    /// Until VCI Version 3.1.4.1784 there is a bug in the .NET API which
    /// returns always the GUID of the interface. In later versions there
    /// the serial number itself will be returned by the UniqueHardwareID property.
    /// </summary>
    /// <param name="serialNumberGuid">Data read from the VCI.</param>
    /// <returns>The GUID as string or if possible the  serial number as string.</returns>
    static string GetSerialNumberText(ref object serialNumberGuid)
    {
      string resultText;

      // check if the object is really a GUID type
      if (serialNumberGuid.GetType() == typeof(System.Guid))
      {
        // convert the object type to a GUID
        System.Guid tempGuid = (System.Guid)serialNumberGuid;

        // copy the data into a byte array
        byte[] byteArray = tempGuid.ToByteArray();

        // serial numbers starts always with "HW"
        if (((char)byteArray[0] == 'H') && ((char)byteArray[1] == 'W'))
        {
          // run a loop and add the byte data as char to the result string
          resultText = "";
          int i = 0;
          while (true)
          {
            // the string stops with a zero
            if (byteArray[i] != 0)
              resultText += (char)byteArray[i];
            else
              break;
            i++;

            // stop also when all bytes are converted to the string
            // but this should never happen
            if (i == byteArray.Length)
              break;
          }
        }
        else
        {
          // if the data did not start with "HW" convert only the GUID to a string
          resultText = serialNumberGuid.ToString();
        }
      }
      else
      {
        // if the data is not a GUID convert it to a string
        string tempString = (string) (string) serialNumberGuid;
        resultText = "";
        for (int i=0; i < tempString.Length; i++)
        {
          if (tempString[i] != 0)
            resultText += tempString[i];
          else
            break;
        }
      }

      return resultText;
    }


    //************************************************************************
    /// <summary>
    ///   Finalizes the application 
    /// </summary>
    //************************************************************************
    static void FinalizeApp()
    {
      //
      // Dispose all hold VCI objects.
      //

      // Dispose message reader
      DisposeVciObject(mReader);

      // Dispose message writer 
      DisposeVciObject(mWriter);

      // Dispose CAN channel
      DisposeVciObject(mCanChn);

      // Dispose CAN controller
      DisposeVciObject(mCanCtl);

      // Dispose VCI device
      DisposeVciObject(mDevice);
    }


    //************************************************************************
    /// <summary>
    ///   This method tries to dispose the specified object.
    /// </summary>
    /// <param name="obj">
    ///   Reference to the object to be disposed.
    /// </param>
    /// <remarks>
    ///   The VCI interfaces provide access to native driver resources. 
    ///   Because the .NET garbage collector is only designed to manage memory, 
    ///   but not native OS and driver resources the application itself is 
    ///   responsible to release these resources via calling 
    ///   IDisposable.Dispose() for the obects obtained from the VCI API 
    ///   when these are no longer needed. 
    ///   Otherwise native memory and resource leaks may occure.  
    /// </remarks>
    //************************************************************************
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

    #endregion
  }
}
