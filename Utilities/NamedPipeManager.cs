using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.IO;

namespace Chetch.Utilities
{
    public static class NamedPipeManager
    {
        public const int CLOSE_PIPE = 1;
        public const int WAIT_FOR_NEXT_CONNECTION = 2;
        public const int SECURITY_EVERYONE = 1;
        public const int DEFAULT_BUFFER_IN = 4096;
        public const int DEFAULT_BUFFER_OUT = 4096;

        private struct PipeInfo
        {
            public String Name { get; set; }
            public PipeDirection Direction { get; set; }
            public PipeSecurity Security { get; set; }
            public PipeStream Stream { get; set; }
            public Func<NamedPipeServerStream, int> OnClientConnect { get; set; }
        }

        public static PipeSecurity GetSecurity(int securityMode)
        {
            PipeSecurity security = new PipeSecurity();

            switch (securityMode)
            {
                case SECURITY_EVERYONE:
                    PipeAccessRule psEveryone = new PipeAccessRule("Everyone", PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow);
                    security.AddAccessRule(psEveryone);
                    break;
            }

            return security;
        }

        public static NamedPipeServerStream Create(String pipeName, PipeDirection direction, PipeSecurity security, Func<NamedPipeServerStream, int> OnClientConnect, int maxServerInstances = 1, int inBufferSize = DEFAULT_BUFFER_IN, int outBufferSize = DEFAULT_BUFFER_OUT)
        {
            NamedPipeServerStream pipeServer = new NamedPipeServerStream(pipeName,
                                                                        direction,
                                                                        maxServerInstances,
                                                                        PipeTransmissionMode.Byte,
                                                                        PipeOptions.Asynchronous,
                                                                        inBufferSize,
                                                                        outBufferSize,
                                                                        security);
            if (pipeServer != null)
            {
                var pipeInfo = new PipeInfo();
                pipeInfo.Name = pipeName;
                pipeInfo.Direction = direction;
                pipeInfo.Security = security;
                pipeInfo.Stream = pipeServer;
                pipeInfo.OnClientConnect = OnClientConnect;
         
                pipeServer.BeginWaitForConnection(new AsyncCallback(WaitForClientConnection), pipeInfo);
            }

            return pipeServer;
        }

        static void WaitForClientConnection(IAsyncResult result)
        {
            PipeInfo pipeInfo = (PipeInfo)result.AsyncState;
            NamedPipeServerStream pipeServer = (NamedPipeServerStream)pipeInfo.Stream;

            // End waiting for the connection
            pipeServer.EndWaitForConnection(result);

            //do delegate function here
            int ret = WAIT_FOR_NEXT_CONNECTION;
            if (pipeInfo.OnClientConnect != null && pipeServer.IsConnected)
            {
                ret = pipeInfo.OnClientConnect(pipeServer);
            }

            switch (ret)
            {
                case CLOSE_PIPE:
                    pipeServer.Close();
                    pipeServer.Dispose();
                    pipeServer = null;
                    break;

                case WAIT_FOR_NEXT_CONNECTION:
                    if (pipeServer.IsConnected)
                    {
                        try
                        {
                            pipeServer.BeginWaitForConnection(new AsyncCallback(WaitForClientConnection), pipeInfo);
                        }
                        catch (IOException)
                        {
                            pipeServer = Create(pipeInfo.Name, pipeInfo.Direction, pipeInfo.Security, pipeInfo.OnClientConnect);
                        }
                    }
                    else
                    {
                        pipeServer.Close();
                        pipeServer.Dispose();
                        pipeServer = null;
                        pipeServer = Create(pipeInfo.Name, pipeInfo.Direction, pipeInfo.Security, pipeInfo.OnClientConnect);

                    }
                    break;
            }
        }
    }
}
