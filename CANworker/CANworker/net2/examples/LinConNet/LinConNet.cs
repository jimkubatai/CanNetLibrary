//////////////////////////////////////////////////////////////////////////
// IXXAT Automation GmbH
//////////////////////////////////////////////////////////////////////////
/**
  Demo application for the IXXAT VCI .NET-API.

  @file "LinConNet.cs"

  @note 
     This demo demonstrates the following VCI features
     - adapter selection
     - controller initialization 
     - creation of a message channel
     - transmission / reception of LIN messages
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
using Ixxat.Vci3.Bal.Lin;


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
    ///   Reference to the LIN controller.
    /// </summary>
    static ILinControl mLinCtl;

    /// <summary>
    ///   Reference to the LIN communication monitor.
    /// </summary>
    static ILinMonitor mLinMon;

    /// <summary>
    ///   Reference to the message reader of the LIN monitor.
    /// </summary>
    static ILinMessageReader mReader;

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
      Console.WriteLine(" initializes the LIN with 19200 bit/s as slave node");
      Console.WriteLine(" shows all received messages");
      Console.WriteLine(" Quit the application with ESC\n");

      Console.Write(" Select Adapter...\r");
      if (SelectDevice())
      {
        Console.WriteLine(" Select Adapter.......... OK !\n");

        Console.Write(" Initialize LIN...\r");
        if (InitSocket())
        {
          Console.WriteLine(" Initialize LIN............ OK !\n");

          //
          // start the receive thread
          //
          rxThread = new Thread(new ThreadStart(ReceiveThreadFunc));
          rxThread.Start();

          //
          // wait for keyboard hit
          //
          while (!Console.KeyAvailable)
          {
            Thread.Sleep(100);
          }

          //
          // tell receive thread to quit
          //
          Interlocked.Exchange(ref mMustQuit, 1);

          //
          // Wait for termination of receive thread
          //
          rxThread.Join();

          Console.Write("\n Free VCI - Resources...\r");
          FinalizeApp();
          Console.WriteLine(" Free VCI - Resources........ OK !\n");
        }
      }

      Console.Write(" Done");
      Console.ReadLine();
    }

    #endregion

    #region Device selection

    //************************************************************************
    /// <summary>
    ///   Selects the first LIN adapter.
    /// </summary>
    /// <return> true if succeeded, false otherwise</return>
    //************************************************************************
    static bool SelectDevice()
    {
      bool              succeeded     = true;
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
      }
      catch (Exception)
      {
        Console.WriteLine("Error: No VCI device installed");
        succeeded = false;
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

      return succeeded;
    }

    #endregion

    #region Opening socket

    //************************************************************************
    /// <summary>
    ///   Opens the specified socket, creates a message monitor, initializes
    ///   and starts the LIN controller.
    /// </summary>
    /// <return> true if succeeded, false otherwise</return>
    //************************************************************************
    static bool InitSocket()
    {
      bool        succeeded = true;
      IBalObject  bal       = null;

      try
      {
        //
        // Open bus access layer
        //
        bal = mDevice.OpenBusAccessLayer();

        //
        // Look for a LIN socket resource
        //
        Byte portNo = 0xFF;
        foreach(IBalResource resource in bal.Resources)
        {
          if (resource.BusType == VciBusType.Lin)
          {
            portNo = resource.BusPort;
          }
          resource.Dispose();
        }

        //
        // Open a message monitor for the LIN controller
        //
        mLinMon = bal.OpenSocket(portNo, typeof(ILinMonitor)) as ILinMonitor;

        // Initialize the message monitor
        mLinMon.Initialize(1024, false);

        // Get a message reader object
        mReader = mLinMon.GetMessageReader();

        // Initialize message reader
        mReader.Threshold = 1;

        // Create and assign the event that's set if at least one message
        // was received.
        mRxEvent = new AutoResetEvent(false);
        mReader.AssignEvent(mRxEvent);

        // Activate the message monitor
        mLinMon.Activate();


        //
        // Open the LIN controller
        //
        mLinCtl = bal.OpenSocket(portNo, typeof(ILinControl)) as ILinControl;

        // Initialize the LIN controller
        LinInitLine initData = new LinInitLine();
        initData.Bitrate = LinBitrate.Lin19200Bit;
        initData.OperatingMode = LinOperatingModes.Slave;
        mLinCtl.InitLine(initData);

        // Start the LIN controller
        mLinCtl.StartLine();
      }
      catch (Exception exc)
      {
        Console.WriteLine("Error: Initializing socket failed : " + exc.Message);
        succeeded = false;
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

    #region Message reception

    //************************************************************************
    /// <summary>
    ///   This method is the works as receive thread.
    /// </summary>
    //************************************************************************
    static void ReceiveThreadFunc()
    {
      LinMessage linMessage;

      do
      {
        // Wait 100 msec for a message reception
        if (mRxEvent.WaitOne(100, false))
        {
          // read a LIN message from the receive FIFO
          while (mReader.ReadMessage(out linMessage))
          {
            switch (linMessage.MessageType)
            {
              //
              // show data frames
              //
              case LinMessageType.Data:
                {
                  Console.Write("\nTime: {0,10}  ID: {1,3}  DLC: {2,1}  Data:",
                                linMessage.TimeStamp,
                                linMessage.ProtId,
                                linMessage.DataLength);

                  for (int index = 0; index < linMessage.DataLength; index++)
                  {
                    Console.Write(" {0,2:X}", linMessage[index]);
                  }
                  break;
                }

              //
              // show informational frames
              //
              case LinMessageType.Info:
                {
                  switch ((LinMsgInfoValue)linMessage[0])
                  {
                    case LinMsgInfoValue.Start:
                      Console.Write("\nLIN started...");
                      break;
                    case LinMsgInfoValue.Stop:
                      Console.Write("\nLIN stopped...");
                      break;
                    case LinMsgInfoValue.Reset:
                      Console.Write("\nLIN reseted...");
                      break;
                  }
                  break;
                }

              //
              // show error frames
              //
              case LinMessageType.Error:
                {
                  switch ((LinMsgError)linMessage[0])
                  {
                    case LinMsgError.Bit:
                      Console.Write("\nbit error...");
                      break;
                    case LinMsgError.Crc:
                      Console.Write("\nCRC error...");
                      break;
                    case LinMsgError.Other:
                      Console.Write("\nother error...");
                      break;
                    case LinMsgError.NoBus:
                      Console.Write("\nno bus activity...");
                      break;
                    case LinMsgError.Parity:
                      Console.Write("\nparity error of the identifier...");
                      break;
                    case LinMsgError.SlaveNoResponse:
                      Console.Write("\nslave does not respond...");
                      break;
                    case LinMsgError.Sync:
                      Console.Write("\ninvalid synchronization field...");
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

      // Dispose LIN monitor
      DisposeVciObject(mLinMon);

      // Dispose LIN controller
      DisposeVciObject(mLinCtl);

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
