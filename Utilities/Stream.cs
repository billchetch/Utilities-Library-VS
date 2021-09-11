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

    public class SerialPortX : SerialPorts.SerialPort, IStream
    {
        public SerialPortX(String port, int baud) : base(port, baud)
        {

            ReadTimeout = 100;
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


            Connect(GetEndPoint());

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
        private bool _localRequestedCTS = false;
        private bool _remoteRequestedCTS = false;
        private DateTime _lastRemoteCTSRequest;

        private int _bytesSent = 0;
        private int _bytesReceived = 0;
        private int _uartLocalBufferSize = 0;
        private int _uartRemoteBufferSize = 0;
        private bool _localReset = false;
        private bool _remoteReset = false;


        private Object _writeLock = new Object();
        private Object _sendLock = new Object();
        public List<byte> ReceiveBuffer { get; internal set; } = new List<byte>();


        private List<byte> _sendBuffer = new List<byte>();
        private Object _sendBufferLock = new Object();
        private Thread _sendThread;
        private Thread _receiveThread;

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

        public delegate bool ReadyToReceiveHandler(StreamFlowController sfc, bool request4cts);
        public ReadyToReceiveHandler ReadyToReceive = null;

        public bool IsOpen => Stream != null && Stream.IsOpen;
        public bool IsReady => IsOpen && _localReset && _remoteReset;

        public bool IsOpening => _opening;
        public bool IsClosing => _closing;

        public int BytesSent => _bytesSent;
        public int BytesReceived => _bytesReceived;

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

                //send thread
                if (_sendThread != null)
                {
                    while (_sendThread.IsAlive)
                    {
                        Thread.Sleep(100);
                    }
                }
                _sendThread = new Thread(Send);
                _sendThread.Name = "SFCSend";
                _sendThread.Start();

                //receive thread
                if (_receiveThread != null)
                {
                    while (_receiveThread.IsAlive)
                    {
                        Thread.Sleep(100);
                    }
                }
                _receiveThread = new Thread(Receive);
                _receiveThread.Name = "SFCReceive";
                _receiveThread.Start();

                Reset(true);
            } finally
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
            _remoteReset = false;
            _localReset = false;
            Reset(false);

            _closing = false;
        }

        public void Reset(bool sendCommandByte = false)
        {
            lock (_sendBufferLock)
            {
                _sendBuffer.Clear();
            }
            ReceiveBuffer.Clear();
            if (sendCommandByte)
            {
                _remoteReset = false;
                SendCommand(Command.RESET); //will reset remote
            }
            while (_sendBuffer.Count > 0) { }; //wait for the send buffer to empty
            _cts = true;
            _bytesSent = 0;
            _bytesReceived = 0;
            _localRequestedCTS = false;
            _remoteRequestedCTS = false;
            SendEvent(Event.RESET);
            _localReset = true;

            //diagnostics
            
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
            return byteCount == (bufferSize - 2);
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
                    int n = Read(readBuffer, 0, readBuffer.Length);
                    if (n < 0)
                    {
                        SendCTS();
                    }
                    else
                    {
                        for (int i = 0; i < n; i++)
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
                                        Reset(false); //do not send command for remote to reset
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
                                        _remoteReset = true;
                                        break;

                                    case (byte)Event.CTS_TIMEOUT:
                                        _remoteRequestedCTS = true;
                                        _lastRemoteCTSRequest = DateTime.Now;
                                        break;

                                    case (byte)Event.CTS_REQUEST_TIMEOUT:
                                        _localRequestedCTS = false;
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
                                _bytesReceived++;
                            }
                            else if (IsSystemByte(b))
                            {
                                isData = false;
                                if (b != CTS_BYTE && b != EVENT_BYTE && b != COMMAND_BYTE) _bytesReceived++;

                                switch (b)
                                {
                                    case SLASH_BYTE:
                                        slashed = true;
                                        //Console.WriteLine("<- SLASH");
                                        break;

                                    case PAD_BYTE:
                                        break;

                                    case END_BYTE:
                                        if (DataBlockReceived != null)
                                        {
                                            //we runs this as a task in case the handler sends messages but is blocked cos
                                            //this thread hasn't processed a CTS_BYTE
                                            var args = new DataBlockArgs(ReceiveBuffer);
                                            Task.Run(() =>
                                            {
                                                DataBlockReceived(this, args);
                                            });
                                        }
                                        ReceiveBuffer.Clear();
                                        break;

                                    case COMMAND_BYTE:
                                        rcommand = true;
                                        break;


                                    case EVENT_BYTE:
                                        revent = true;
                                        break;

                                    case CTS_BYTE:
                                        if (!_cts) //we check that _cts is indeed false so tha we don't reset _bytesSent unnecessarily
                                        {
                                            Console.WriteLine("<--- CTS (bytes sent/received {0}/{1})", _bytesSent, _bytesReceived);
                                            lock (_writeLock)
                                            {
                                                _bytesSent = 0; //Note: important tha we set _bytesSent BEFORE _cts so that the send loop starts again with _bytesSent already reset
                                                _cts = true;
                                                _localRequestedCTS = false; //request has been granted
                                            }
                                        }
                                        break;

                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                _bytesReceived++;
                                isData = true;
                            }

                            //temp
                            if (isData && IsReady)
                            {
                                ReceiveBuffer.Add(b);
                                //_prevByte = b;
                            }

                            //Flow control
                            SendCTS();
                        } //end read bytes loop
                    }
                    if (_remoteRequestedCTS)
                    {
                        if (isReadyToReceive(true))
                        {
                            SendCTS(true);
                        } 
                        else 
                        {
                            double ms = (double)(DateTime.Now.Ticks - _lastRemoteCTSRequest.Ticks) / (double)TimeSpan.TicksPerMillisecond;
                            if(ms > 2000)
                            {
                                SendEvent(Event.CTS_REQUEST_TIMEOUT);
                                _remoteRequestedCTS = false;
                            }
                        }
                    }
                } // end  while is open loop
            } 
            catch (Exception e)
            {
                if (!IsClosing)
                {
                    Task.Run(() =>
                    {
                        if (IsOpen) Close();
                        OnStreamError(e);
                    });
                }
            }
            Console.WriteLine("Receive thread ended");
        }

        protected void Send()
        {
            try
            {
                while (Stream.IsOpen)
                {
                    if (_sendBuffer.Count > 0)
                    {
                        lock (_sendBufferLock)
                        {
                            Stream.Write(_sendBuffer.ToArray(), 0, _sendBuffer.Count);
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
                        if (IsOpen) Close();
                        OnStreamError(e);
                    });
                }
            }

            Console.WriteLine("Send thread ended");
        }

        protected int Read(byte[] buffer, int offset, int count)
        {
            int n = Stream.Read(buffer, offset, count);
            return n;
        }

        public void WriteByte(byte b, bool count = true)
        {
            lock (_sendBufferLock)
            {
                _sendBuffer.Add(b);
            }
            if (count)
            {
                //Flow control
                _bytesSent++;
                if (requiresCTS(_bytesSent, _uartRemoteBufferSize))
                {
                    //Console.WriteLine("*** Setting CTS to false after sending {0},  i = {1}, bytesSent = {2}", b, i, _bytesSent);
                    _cts = false;
                    _lastCTSrequired = DateTime.Now;
                }
            }
        }

        private void sendByte(byte b, bool count = true)
        {
            lock (_writeLock)
            {
                WriteByte(b, count);
            }
        }

        private bool isReadyToReceive(bool request4cts)
        {
            if (ReadyToReceive != null)
            {
                return ReadyToReceive(this, request4cts);
            }
            else
            {
                return true;
            }
        }

        public bool SendCTS(bool overrideFlowControl = false)
        {
            if ((requiresCTS(_bytesReceived, _uartLocalBufferSize) && isReadyToReceive(false)) || overrideFlowControl)
            {
                //Console.WriteLine("----> CTS ... sent/received {0}/{1}", _bytesSent, _bytesReceived);
                _bytesReceived = 0;
                _remoteRequestedCTS = false;
                sendByte(CTS_BYTE, false);
                return true;
            } 
            else
            {
                return false;
            }
        }


        public void SendCommand(byte b)
        {
            lock (_writeLock)
            {
                WriteByte(COMMAND_BYTE, false);
                WriteByte(b, false);
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
                WriteByte(EVENT_BYTE, false);
                WriteByte(b, false);
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

                        if (CTSTimeout > 0 && !_localRequestedCTS) //we can only request once
                        {
                            ms = (double)(DateTime.Now.Ticks - _lastCTSrequired.Ticks) / (double)TimeSpan.TicksPerMillisecond;
                            if (ms > CTSTimeout && !_cts)
                            {
                                SendEvent(Event.CTS_TIMEOUT);
                                _localRequestedCTS = true;
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
                                if (requiresCTS(_bytesSent + 1, _uartRemoteBufferSize))
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
                            WriteByte(b);
                        } while (_cts && i < bytes2send.Count); //loop a block
                    } //end write lock
                } //end loop throught byte blocks
            } //end send lock
            //Console.WriteLine("--> Sent message of length {0} (using {1} slashes), i = {2}, bytesSent={3} ", bytes2send.Count, slashCount, i, _bytesSent);
        }
    }
}
