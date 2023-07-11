using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace Chetch.Utilities.Streams
{
    public interface IStream
    {
        void Open();
        void Close();

        bool IsOpen
        {
            get;
        }

        int Read(byte[] buffer, int offset, int count);
        void Write(byte[] buffer, int offset, int count);
    }

    [System.ComponentModel.DesignerCategory("Code")]
    public class SerialPortX : SerialPorts.SerialPort, IStream
    {
        public SerialPortX(String port, int baud) : base(port, baud)
        {

            ReadTimeout = 100;
            WriteTimeout = 100;
        }

        new public int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return base.Read(buffer, offset, count);
            }
            catch (TimeoutException)
            {
                return -1;
            }
        }
    }

    public class TCPClientStream : TcpClient, IStream
    {
        private bool _open = false;

        public bool IsOpen => _open && Connected;

        

        public IPEndPoint RemoteEndPoint { get; set; } = null;

        public TCPClientStream(IPEndPoint remoteEndPoint)
        {
            RemoteEndPoint = remoteEndPoint;
        }

        virtual protected IPEndPoint GetEndPoint()
        {
            return RemoteEndPoint;
        }

        virtual protected void ConnectToEndPoint()
        {
            Connect(GetEndPoint());
        }

        new public void Close()
        {
            var stream = GetStream();
            stream.Close();
            base.Close();
            _open = false;
        }

        public void Open()
        {
            if (IsOpen) return;


            ConnectToEndPoint();

            NoDelay = true;
            var stream = GetStream();
            stream.ReadTimeout = 100;
            stream.WriteTimeout = 100;


            Thread.Sleep(200);

            _open = true;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                var stream = GetStream();
                return stream.Read(buffer, offset, count);
            }
            catch (System.IO.IOException e)
            {
                if (e.InnerException != null && e.InnerException is SocketException)
                {
                    var se = (SocketException)e.InnerException;
                    if (se.SocketErrorCode == SocketError.TimedOut)
                    {
                        return -1;
                    }
                }
                throw e;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            var stream = GetStream();
            stream.Write(buffer, offset, count);
        }
    }

    public class StreamFlowController
    {

        public class StreamErrorArgs : EventArgs
        {
            public ErrorCode Error { get; internal set; }
            public Exception Exception { get; internal set; }

            public StreamErrorArgs(ErrorCode error, Exception e)
            {
                Error = error;
                Exception = e;
            }

            public StreamErrorArgs(ErrorCode error) : this(error, null)
            {}
        }

        public class CommandByteArgs : EventArgs
        {
            public byte CommandByte { get; internal set; }

            public CommandByteArgs(byte cb)
            {
                CommandByte = cb;
            }
        }

        public class EventByteArgs : EventArgs
        {
            public byte EventByte { get; internal set; }
            

            public EventByteArgs(byte eb)
            {
                EventByte = eb;
            }
        }

        public class DataBlockArgs : EventArgs
        {
            public List<byte> DataBlock { get; internal set; } = new List<byte>();

            public DataBlockArgs(List<byte> dataBlock)
            {
                DataBlock.AddRange(dataBlock);
            }
        }

        public const byte COMMAND_BYTE = 0x63;
        public const byte CTS_BYTE = 0x74;
        public const byte SLASH_BYTE = 0x5c;
        public const byte PAD_BYTE = 0x70;
        public const byte END_BYTE = 0x64;
        public const byte EVENT_BYTE = 0x73;

        public enum Command
        {
            RESET = 1,
            DEBUG_ON = 2,
            DEBUG_OFF = 3,
            RESET_RECEIVE_BUFFER = 4,
            RESET_SEND_BUFFER = 5,
            PING = 6,
            REQUEST_STATUS = 100, //100 and above are general but require user definition (by convention above 200 is completely user-specific)
        };

        public enum Event
        {
            RESET = 1,
            RECEIVE_BUFFER_FULL = 2,
            RECEIVE_BUFFER_OVERFLOW_ALERT = 3,
            SEND_BUFFER_FULL = 4,
            SEND_BUFFER_OVERFLOW_ALERT = 5,
            CHECKSUM_FAILED = 6,
            UNKNOWN_ERROR = 7,
            ALL_OK = 8,
            CTS_TIMEOUT = 9,
            MAX_DATABLOCK_SIZE_EXCEEDED = 10,
            CTS_REQUEST_TIMEOUT = 11,
            PING_RECEIVED = 12,
        };

        public enum ErrorCode
        {
            UNKNOWN_ERROR = 1,
            UNEXPECTED_DISCONNECT = 2,
        }

        public IStream Stream { get; set;  }
        private bool _cts = true;
        public int CTSTimeout { get; set; } = -1;
        private DateTime _lastCTSrequired;
        private bool _sentCTSTimeout = false;
        
        private ulong _bytesSent = 0;
        private int _bytesSentSinceCTS = 0;
        private ulong _bytesReceived = 0;
        private int _bytesReceivedSinceCTS = 0;
        private int _uartLocalBufferSize = 0;
        private int _uartRemoteBufferSize = 0;
        private bool _localReset = false;
        private bool _remoteReset = false;


        private Object _writeLock = new Object();
        private Object _sendLock = new Object();
        public List<byte> ReceiveBuffer { get; internal set; } = new List<byte>();
        private Queue<DataBlockArgs> _dataBlocks = new Queue<DataBlockArgs>();
        private Object _dataBlocksLock = new Object();

        private List<byte> _sendBuffer = new List<byte>();
        private Object _sendBufferLock = new Object();


        private Thread _sendThread;
        private EventWaitHandle _sendWaithHandle;
        private Thread _receiveThread;
        private Thread _processThread;
        private EventWaitHandle _processsWaitHandle;

        private bool _opening = false;
        private bool _closing = false;

        //public int TotalBytesSent { get; set; } = 0;
        //public int TotalBytesReceived { get; set; } = 0;
        //private int _prevByte = -1;
        //List<byte> _receiveHistory = new List<byte>();
        private Object _streamErrorLock = new Object();
        public event EventHandler<StreamErrorArgs> StreamError;
        public event EventHandler<DataBlockArgs> DataBlockReceived;
        public event EventHandler<CommandByteArgs> CommandByteReceived;
        public event EventHandler<EventByteArgs> EventByteReceived;
        public event EventHandler<EventByteArgs> EventByteSent;

        public delegate bool ReadyToReceiveHandler(StreamFlowController sfc);
        public ReadyToReceiveHandler ReadyToReceive = null;

        public bool IsOpen => Stream != null && Stream.IsOpen;
        public bool IsReady => IsOpen && _localReset && _remoteReset;

        public bool IsOpening => _opening;
        public bool IsClosing => _closing;

        
        public ulong BytesSent => _bytesSent;
        public ulong BytesReceived => _bytesReceived;

        public StreamFlowController(IStream stream, int localBufferSize, int remoteBufferSize)
        {
            Stream = stream;
            _uartLocalBufferSize = localBufferSize;
            _uartRemoteBufferSize = remoteBufferSize;
        }

        public StreamFlowController(int localBufferSize, int remoteBufferSize) : this(null, localBufferSize, remoteBufferSize) { }

        public void Open()
        {
            if (Stream == null) throw new InvalidOperationException("Stream object is null");
            if (IsOpen) throw new InvalidOperationException("Stream is already open");
            if (IsOpening) throw new InvalidOperationException("Stream is in the process of opening");
            if (IsClosing) throw new InvalidOperationException("Stream is in the process of closing");

            _opening = true;
            _remoteReset = false;
            _localReset = false;
            try
            {
                Stream.Open();

                //Send thread
                if (_sendThread != null)
                {
                    while (_sendThread.IsAlive)
                    {
                        _sendThread.Abort();
                        Thread.Sleep(100);
                    }
                }
                _sendThread = new Thread(Send);
                _sendThread.Name = "SFCSend";
                _sendThread.IsBackground = true;
                _sendThread.Priority = ThreadPriority.BelowNormal;
                _sendWaithHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
                
                //Receive thread
                if (_receiveThread != null)
                {
                    while (_receiveThread.IsAlive)
                    {
                        _receiveThread.Abort();
                        Thread.Sleep(100);
                    }
                }
                _receiveThread = new Thread(Receive);
                _receiveThread.Name = "SFCReceive";
                _receiveThread.IsBackground = true;
                _receiveThread.Priority = ThreadPriority.BelowNormal;


                //Process thread
                if (_processThread != null)
                {
                    while (_processThread.IsAlive)
                    {
                        _processThread.Abort();
                        Thread.Sleep(100);
                    }
                }
                _processThread = new Thread(Process);
                _processThread.Name = "SFCProcess";
                _processThread.IsBackground = true;
                _processThread.Priority = ThreadPriority.BelowNormal;
                _processsWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
                
                //Console.WriteLine("Reset (true, true) called from Open ");
                Reset(true, true);

                //start the receive thread
                _sendThread.Start();
                _receiveThread.Start();
                _processThread.Start();

            }
            finally
            {
                _opening = false;
            }
        }

        public void Close()
        {
            if (IsOpening) throw new InvalidOperationException("Stream is in the process of opening");
            if (IsClosing) throw new InvalidOperationException("Stream is in the process of closing");

            _closing = true;
            Stream.Close();
            if (_sendThread != null)
            {
                _sendWaithHandle.Set();
                while (_sendThread.IsAlive)
                {
                    Thread.Sleep(100);
                }
            }
            if (_receiveThread != null)
            {
                while (_receiveThread.IsAlive)
                {
                    Thread.Sleep(100);
                }
            }
            if (_processThread != null)
            {
                _processsWaitHandle.Set();
                while (_processThread.IsAlive)
                {
                    Thread.Sleep(100);
                }
            }
            _remoteReset = false;
            _localReset = false;
            Reset(false, true);

            _closing = false;
        }

        public void Reset(bool sendCommandByte, bool sendEventByte)
        {
            //Console.WriteLine("Resetting...");
            lock (_sendBufferLock)
            {
                _sendBuffer.Clear();
            }
            ReceiveBuffer.Clear();
            _dataBlocks.Clear();

            _cts = true;
            _bytesSent = 0;
            _bytesSentSinceCTS = 0;
            _bytesReceived = 0;
            _bytesReceivedSinceCTS = 0;
            _sentCTSTimeout = false;
            _localReset = true;


            if (sendCommandByte)
            {
                //Console.WriteLine("Sending RESET command ... Bytes sent / received since last CTS sent: {0} / {1}", _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                SendCommand(Command.RESET); //will reset remote
            }
            
            //Console.WriteLine("Sending RESET Event ... Bytes sent / received since last CTS sent: {0} / {1}", _bytesSentSinceCTS, _bytesReceivedSinceCTS);
            if (sendEventByte)
            {
                //Console.WriteLine("Sending RESET Event ... Bytes sent / received since last CTS sent: {0} / {1}", _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                SendEvent(Event.RESET);
            }
        }


        public bool IsSystemByte(byte b)
        {
            switch (b)
            {
                case COMMAND_BYTE:
                case CTS_BYTE:
                case SLASH_BYTE:
                case PAD_BYTE:
                case END_BYTE:
                case EVENT_BYTE:
                    return true;

                default:
                    return false;
            }
        }

        private bool requiresCTS(int byteCount, int bufferSize)
        {
            if (bufferSize <= 0) return false;
            return byteCount >= (bufferSize - 2); //The 2 here is to allow for a command byte such as reset to be sent regardless
        }

        protected void OnStreamError(Exception e)
        {
            if(StreamError != null)
            {
                lock (_streamErrorLock)
                {
                    ErrorCode error = IsOpen ? ErrorCode.UNKNOWN_ERROR : ErrorCode.UNEXPECTED_DISCONNECT;
                    var eargs = new StreamErrorArgs(error, e);
                    StreamError(this, eargs);
                }
            }
        }

        protected void Receive()
        {
            bool rcommand = false;
            bool revent = false;
            bool isData = false;
            bool slashed = false;
            byte[] readBuffer = new byte[1024];
            //int prevRecvByte = -1;

            try
            {
                while (Stream.IsOpen)
                {
                    //readBuffer = new byte[1024];
                    int bytes2read = Read(readBuffer, 0, readBuffer.Length);

                    if (bytes2read < 0)
                    {
                        //nothing to read
                    }
                    else
                    {
                        for (int i = 0; i < bytes2read; i++)
                        {
                            byte b = readBuffer[i];
                            //Console.WriteLine("<--- Received: {0}", b);
                            if (rcommand)
                            {
                                isData = false;
                                rcommand = false; //we do not count
                                switch (b)
                                {
                                    case (byte)Command.RESET:
                                        //Console.WriteLine("Reset command received .. caling Reset(false, false)... Bytes sent / received since last CTS sent: {0} / {1}", _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                                        _remoteReset = false;
                                        Reset(false, false); //do not send command for remote to reset
                                        break;
                                }
                                if (CommandByteReceived != null)
                                {
                                    CommandByteReceived(this, new CommandByteArgs(b));
                                }
                            }
                            else if (revent)
                            {
                                isData = false;
                                revent = false; //we do not count
                                switch (b)
                                {
                                    case (byte)Event.RESET:
                                        //Console.WriteLine("Remote RESET Event received ... Bytes sent / received since last CTS sent: {0} / {1}", _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                                        bool ready = IsReady;
                                        _remoteReset = true;
                                        if(!ready && IsReady)
                                        {
                                            //Console.WriteLine("READY ... Bytes sent / received since last CTS sent: {0} / {1}", _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                                            _bytesSentSinceCTS = 0;
                                            _bytesReceivedSinceCTS = -1; //we set to -1 because it is incremented below
                                        }
                                        break;

                                    case (byte)Event.CTS_TIMEOUT:
                                        //Console.WriteLine("<----- Received CTS Timeout");
                                        break;

                                    case (byte)Event.CTS_REQUEST_TIMEOUT:
                                        //TODO: complete this
                                        break;
                                }
                                if (EventByteReceived != null)
                                {
                                    EventByteReceived(this, new EventByteArgs(b));
                                }
                            }
                            else if (slashed)
                            {
                                isData = true;
                                slashed = false;
                            }
                            else if (IsSystemByte(b))
                            {
                                isData = false;
                                
                                switch (b)
                                {
                                    case SLASH_BYTE:
                                        slashed = true;
                                        //Console.WriteLine("<- SLASH");
                                        break;

                                    case PAD_BYTE:
                                        break;

                                    case END_BYTE:
                                        lock (_dataBlocksLock)
                                        {
                                            _dataBlocks.Enqueue(new DataBlockArgs(ReceiveBuffer));
                                        }
                                        ReceiveBuffer.Clear();
                                        _processsWaitHandle.Set(); //release Process thread
                                        break;

                                    case COMMAND_BYTE:
                                        rcommand = true;
                                        break;


                                    case EVENT_BYTE:
                                        revent = true;
                                        break;

                                    case CTS_BYTE:
                                        //Console.WriteLine("<<--- Received CTS byte (bytes sent/received {0}/{1})", _bytesSentSinceCTS, _bytesReceivedSinceCTS);

                                        lock (_writeLock)
                                        {
                                            _bytesSentSinceCTS = 0;
                                            _cts = true;
                                            _sentCTSTimeout = false; //request has been granted
                                        }
                                        break;

                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                isData = true;
                            }

                            //temp
                            if (isData && IsReady)
                            {
                                ReceiveBuffer.Add(b);
                            }

                            _bytesReceived++;
                            if(_uartLocalBufferSize > 0)
                            {
                                _bytesReceivedSinceCTS++;
                            }
                        } //end read bytes loop
                        //Console.WriteLine("<--- bytes received {0}, bytes since cts sent / received: {1} / {2}", bytes2read, _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                    } //end if there are bytes to read 

                    //so here we have read bytes2read from the uart buffer  || _receivedCTSTimeout
                    if (((_uartLocalBufferSize > 0 && requiresCTS(_bytesReceivedSinceCTS, _uartLocalBufferSize))) && isReadyToReceive())
                    {
                        //Console.WriteLine("---->> Sending CTS byte...bytes since cts sent / received: {0} / {1}", _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                        SendCTS();
                        _bytesReceivedSinceCTS = 0;
                    }

                } // end  while is open loop
            } 
            catch (Exception e)
            {
                if (!IsClosing)
                {
                    Task.Run(() =>
                    {
                        if (IsOpen)
                        {
                            try
                            {
                                Close();
                            } catch {}
                        }
                        OnStreamError(e);
                    });
                }
            }
            //Console.WriteLine("Receive thread ended");
        }

        protected void Process()
        {
            try
            {
                List<DataBlockArgs> blocksReceived = new List<DataBlockArgs>();
                
                while (Stream.IsOpen)
                {
                    _processsWaitHandle.WaitOne();

                    lock (_dataBlocksLock)
                    {
                        while (_dataBlocks.Count > 0)
                        {
                            blocksReceived.Add(_dataBlocks.Dequeue());
                        }
                    }
                    if (DataBlockReceived != null)
                    {
                        foreach (var args in blocksReceived)
                        {
                            DataBlockReceived(this, args);
                        }
                    }
                    blocksReceived.Clear();

                }
            }
            catch (Exception e)
            {
                if (!IsClosing)
                {
                    Task.Run(() =>
                    {
                        if (IsOpen)
                        {
                            try
                            {
                                Close();
                            }
                            catch {}
                        }
                        OnStreamError(e);
                    });
                }
            }
        }

        protected void Send()
        {
            try
            {
                while (Stream.IsOpen)
                {
                    _sendWaithHandle.WaitOne();

                    if (_sendBuffer.Count > 0)
                    {
                        lock (_sendBufferLock)
                        {
                            Stream.Write(_sendBuffer.ToArray(), 0, _sendBuffer.Count);
                            //Console.WriteLine("----> Sending {0} bytes ... bytes since cts sent / received {1} / {2}", _sendBuffer.Count, _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                            _sendBuffer.Clear();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!IsClosing)
                {
                    Task.Run(() =>
                    {
                        if (IsOpen)
                        {
                            try
                            {
                                Close();
                            }
                            catch {}
                        }
                        OnStreamError(e);
                    });
                }
            }

            //Console.WriteLine("Send thread ended");
        }

        //Reading methods
        protected int Read(byte[] buffer, int offset, int count)
        {
            int n = Stream.Read(buffer, offset, count);
            return n;
        }

        //Writing methods
        //All bytes written must ultimately pass through this method
        //chosing to set wait handle will release the send thread
        protected void WriteByte(byte b, bool setWaitHandle = true)
        {
            lock (_sendBufferLock)
            {
                _sendBuffer.Add(b);
            }
            if(setWaitHandle)_sendWaithHandle.Set();

            _bytesSent++;
            if (_uartRemoteBufferSize > 0) //condition for byte counting flow control
            {
                _bytesSentSinceCTS++;
            }
        }

        private void sendByte(byte b)
        {
            lock (_writeLock)
            {
                WriteByte(b);
            }
        }

        private bool isReadyToReceive()
        {
            if (ReadyToReceive != null)
            {
                return ReadyToReceive(this);
            }
            else
            {
                return true;
            }
        }

        public void SendCTS()
        {
             //Console.WriteLine("----> CTS ... sent/received {0}/{1}", _bytesSent, _bytesReceived);
             sendByte(CTS_BYTE);
        }


        public void SendCommand(byte b)
        {
            lock (_writeLock)
            {
                WriteByte(COMMAND_BYTE, false);
                WriteByte(b, true); //release
            }
        }

        public void SendCommand(Command c)
        {
            SendCommand((byte)c);
        }

        public void Ping()
        {
            SendCommand(Command.PING);
        }

        public void SendEvent(byte b)
        {
            if (EventByteSent != null)
            {
                EventByteSent(this, new EventByteArgs(b));
            }
            lock (_writeLock)
            {
                //Console.WriteLine("Send event {0}", b);
                WriteByte(EVENT_BYTE, false);
                WriteByte(b, true);
            }
        }

        public void SendEvent(Event e)
        {
            SendEvent((byte)e);
        }

        
        public void Send(byte[] bytes)
        {
            Send(bytes.ToList());
        }

        public void Send(List<byte> bytes)
        {
            if (bytes.Count == 0)
            {
                throw new Exception("Cannot send a zero length array");
            }
            if (!IsReady)
            {
                OnStreamError(new Exception("Stream is not Ready"));
                return;
            }

            //add slashes and end byte
            List<byte> bytes2send = new List<byte>();
            for (int n = 0; n < bytes.Count; n++)
            {
                byte b = bytes[n];
                if (IsSystemByte(b))
                {
                    bytes2send.Add(SLASH_BYTE);
                }
                bytes2send.Add(b);
            }
            bytes2send.Add(END_BYTE);

            //Console.WriteLine("--> Starting message of length {0} (bytes so far sent {1})", bytes2send.Length, _bytesSent);
            //We use a lock here to ensure that the entire byte list is sent without the risk of competing threads interuppting
            lock (_sendLock) 
            {

                DateTime t;
                bool waiting = false;
                int i = 0;
                bool slashed = false;
                while (i < bytes2send.Count)
                {
                    //1. Wait for Clear to Send

                    t = DateTime.Now;
                    while (!_cts)
                    {
                        if (!IsReady)
                        {
                            OnStreamError(new Exception("Stream is not ready"));
                            return;
                        }
                        waiting = true;
                        double ms = (double)(DateTime.Now.Ticks - t.Ticks) / (double)TimeSpan.TicksPerMillisecond;
                        if (ms >= 12) //this is to reduce hammering the CPU with a while loop
                        {
                            //Console.WriteLine("Consuming too many cycles so sleeping...");
                            Thread.Sleep(1);
                        }

                        if (CTSTimeout > 0 && !_sentCTSTimeout) //we can only request once
                        {
                            ms = (DateTime.Now - _lastCTSrequired).TotalMilliseconds;
                            if (ms > CTSTimeout && !_cts)
                            {
                                _sentCTSTimeout = true;
                                SendEvent(Event.CTS_TIMEOUT);

                                throw new TimeoutException(String.Format("Timeoud out in Send waiting for CTS flag to set to true after {0} ms", ms));
                            }
                        }
                    }
                    if (waiting)
                    {
                        //Console.WriteLine("--> CTS arrived  i = {0}, bytesSent = {1}...", i, _bytesSent);
                        waiting = false;
                    }

                    //2. Write the byte to the underlying buffer
                    lock (_writeLock)
                    {
                        do
                        {
                            byte b = bytes2send[i];
                            if (slashed)
                            {
                                i++;
                                slashed = false;
                            }
                            else if (b == SLASH_BYTE)
                            {
                                if (requiresCTS(_bytesSentSinceCTS + 1, _uartRemoteBufferSize))
                                {
                                    b = PAD_BYTE;
                                }
                                else
                                {
                                    slashed = true;
                                    i++;
                                }
                            }
                            else
                            {
                                i++;
                            }
                            //Console.WriteLine("--> Sending: {0}", b);
                            if (!IsReady)
                            {
                                OnStreamError(new Exception("Stream is not ready"));
                                return;
                            }

                            //write the byte to teh buffer but don't release send thread
                            WriteByte(b, false);

                            //check cts condition
                            if (requiresCTS(_bytesSentSinceCTS, _uartRemoteBufferSize))
                            {
                                //Console.WriteLine("*** Setting CTS to false after sending byte {0}, bytes sent / recv since cts = {1} / {2}", b, _bytesSentSinceCTS, _bytesReceivedSinceCTS);
                                _cts = false;
                                _lastCTSrequired = DateTime.Now;
                            }

                        } while (_cts && i < bytes2send.Count); //loop a block

                        //release send thread
                        _sendWaithHandle.Set();

                    } //end write lock
                } //end loop throught byte blocks
            } //end send lock
            //Console.WriteLine("--> Sent message of length {0} (using {1} slashes), i = {2}, bytesSent={3} ", bytes2send.Count, slashCount, i, _bytesSent);
        }
    }
    
}
