using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

    public class StreamFlowController
    {
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
            TIMEOUT_ALERT_ONLY = 4,
            TIMEOUT_ALERT_AND_SET_CTS = 5,
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
        };

        

        private IStream _stream;
        private bool _cts = true;
        private int _bytesSent = 0;
        private int _bytesReceived = 0;
        private int _uartLocalBufferSize = 0;
        private int _uartRemoteBufferSize = 0;
        
        private Object _writeLock = new Object();
        public List<byte> ReceiveBuffer { get; internal set; } = new List<byte>();


        private List<byte> _sendBuffer = new List<byte>();
        private Object _sendBufferLock = new Object();
        private Thread _sendThread;
        private Thread _receiveThread;

        //public int TotalBytesSent { get; set; } = 0;
        //public int TotalBytesReceived { get; set; } = 0;
        //private int _prevByte = -1;
        //List<byte> _receiveHistory = new List<byte>();

        public event EventHandler DataBlockReceived;
        public event EventHandler<CommandByteArgs> CommandByteReceived;
        public event EventHandler<EventByteArgs> EventByteReceived;

        public delegate bool ReadyToReceiveHandler(StreamFlowController sfc);
        public ReadyToReceiveHandler ReadyToReceive = null;


        /*public SerialPortX(String port, int baud, int localBufferSize, int remoteBufferSize) : base(port, baud)
        {
            _uartLocalBufferSize = localBufferSize;
            _uartRemoteBufferSize = remoteBufferSize;
            ErrorReceived += HandleErrorReceived;
        }

        public SerialPortX(String port, int baud, int bufferSize) : this(port, baud, bufferSize, bufferSize)
        {}*/

        public StreamFlowController(IStream stream, int localBufferSize, int remoteBufferSize)
        {
            _stream = stream;
            _uartLocalBufferSize = localBufferSize;
            _uartRemoteBufferSize = remoteBufferSize;
        }

        public void Open()
        {
            _stream.Open();
            _sendThread = new Thread(Send);
            _sendThread.Start();

            _receiveThread = new Thread(Receive);
            _receiveThread.Start();

            Reset(true, false);
        }


        /*protected void HandleErrorReceived(object sender,
                        SerialErrorReceivedEventArgs e)
        {
            Console.WriteLine("Error: {0}");
        } */

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
            if (byteCount > (bufferSize - 2)) throw new Exception(String.Format("requiresCTS: Bytecount of {0} exceeds buffer size of {1}", byteCount, bufferSize));
            return byteCount == (bufferSize - 2);
        }

        protected void Receive()
        {
            bool rcommand = false;
            bool revent = false;
            bool isData = false;
            bool slashed = false;
            byte[] readBuffer = new byte[1024];
            //int prevRecvByte = -1;

            while (_stream.IsOpen)
            {
                //readBuffer = new byte[1024];
                int n = _stream.Read(readBuffer, 0, readBuffer.Length);
                for (int i = 0; i < n; i++)
                {
                    byte b = readBuffer[i];
                    Console.WriteLine("<--- Received: {0}", b);
                    if (rcommand)
                    {
                        isData = false;
                        rcommand = false; //we do not count

                        if (CommandByteReceived != null)
                        {
                            CommandByteReceived(this, new CommandByteArgs(b));
                        }
                    }
                    else if (revent)
                    {
                        isData = false;
                        revent = false; //we do not count

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
                        if (b != CTS_BYTE && b != EVENT_BYTE) _bytesReceived++;

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
                                    DataBlockReceived(this, null);
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
                                Console.WriteLine("<--- CTS (bytes sent/received {0}/{1})", _bytesSent, _bytesReceived);

                                lock (_writeLock)
                                {
                                    _bytesSent = 0; //Note: important tha we set _bytesSent BEFORE _cts so that the send loop starts again with _bytesSent already reset
                                    _cts = true;
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
                    if (isData)
                    {
                        ReceiveBuffer.Add(b);
                        //_prevByte = b;
                    }

                    //send cts (check function body for conditions
                    sendCTS();
                    
                    //prevRecvByte = b;
                } //end read bytes loop
            } // end  while is open loop
        }

        protected void Send()
        {
            while (_stream.IsOpen)
            {
                if (_sendBuffer.Count > 0)
                {
                    lock (_sendBufferLock)
                    {
                        _stream.Write(_sendBuffer.ToArray(), 0, _sendBuffer.Count);
                        _sendBuffer.Clear();
                    }
                }
            }
        }

        public void WriteByte(byte b, bool count = true)
        {
            lock (_sendBufferLock)
            {
                _sendBuffer.Add(b);
            }
            if (count) _bytesSent++;
        }

        private void sendByte(byte b)
        {
            lock (_writeLock)
            {
                WriteByte(b, b != CTS_BYTE);
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
                return _sendBuffer.Count == 0;
            }
        }

        private bool sendCTS()
        {
            if (requiresCTS(_bytesReceived, _uartLocalBufferSize) && isReadyToReceive())
            {
                //Console.WriteLine("----> CTS ... sent/received {0}/{1}", _bytesSent, _bytesReceived);
                _bytesReceived = 0;
                sendByte(CTS_BYTE);
                return true;
            } 
            else
            {
                return false;
            }
        }


        public void SendCommand(byte b)
        {
            sendByte(COMMAND_BYTE);
            sendByte(b);
        }

        public void SendCommand(Command c)
        {
            SendCommand((byte)c);
        }

        public void SendEvent(byte b)
        {
            sendByte(EVENT_BYTE);
            sendByte(b);
        }

        public void SendEvent(Event e)
        {
            SendEvent((byte)e);
        }

        

        public void Reset(bool resetRemote = false, bool sendEventByte = false)
        {
            lock (_sendBufferLock)
            {
                _sendBuffer.Clear();
            }
            if(resetRemote)SendCommand(Command.RESET);
            while (_sendBuffer.Count > 0) { }; //wait for the send buffer to empty
            _cts = true;
            _bytesSent = 0;
            _bytesReceived = 0;
            if (sendEventByte) SendEvent(Event.RESET);
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
            DateTime t;
            bool waiting = false;

            int i = 0;
            bool slashed = false;
            while (i < bytes2send.Count)
            {
                t = DateTime.Now;
                while (!_cts)
                {
                    if (!waiting)
                    {
                        //Console.WriteLine("--> Waiting for CTS i = {0}, bytesSent = {1}...", i, _bytesSent);
                    }
                    waiting = true;
                    double ms = (double)(DateTime.Now.Ticks - t.Ticks) / (double)TimeSpan.TicksPerMillisecond;
                    if (ms >= 12)
                    {
                        //Console.WriteLine("Consuming too many cycles so sleeping...");
                        Thread.Sleep(1);
                    }
                }
                if (waiting)
                {
                    //Console.WriteLine("--> CTS arrived  i = {0}, bytesSent = {1}...", i, _bytesSent);
                    waiting = false;
                }

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
                        WriteByte(b);

                        if (requiresCTS(_bytesSent, _uartRemoteBufferSize))
                        {
                            //Console.WriteLine("*** Setting CTS to false after sending {0},  i = {1}, bytesSent = {2}", b, i, _bytesSent);
                            _cts = false;
                        }

                    } while (_cts && i < bytes2send.Count); //loop a block
                } //end write lock
            } //end loop throught byte blocks
            //Console.WriteLine("--> Sent message of length {0} (using {1} slashes), i = {2}, bytesSent={3} ", bytes2send.Count, slashCount, i, _bytesSent);
        }
    }
}
