﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace pxNetAdapter
{
    public class Connector : IConnector
    {
        #region Members
        
        public event EventHandler OnConnect;
        public event EventHandler<int> OnReconnect;
        public event EventHandler OnDisconnect;
        public event EventHandler<string> OnMessage;

        private TcpClient m_tcpClient;
        private string m_host;
        private int m_port;

        #endregion

        public Connector(int reconnects=3)
        {
            State = ConnectionStateEnum.Disconnected;
            Reconnects = reconnects;
        }

        #region Properties

        private string Delimiter { get; set; }

        public ConnectionStateEnum State { get; private set; }
        public int Reconnects { get; set; }
        public string SessionId { get; private set; }

        #endregion

        #region Methods

        public void Connect(string host, int port)
        {
            if (State != ConnectionStateEnum.Disconnected)
                return;

            if (string.IsNullOrEmpty(host))
                throw new ArgumentException("host must be a non empty string", "host");

            if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
                throw new ArgumentException(string.Format("port must be between {0} and {1}", IPEndPoint.MinPort, IPEndPoint.MaxPort), "port");

            m_host = host;
            m_port = port;
            Thread socketThread = new Thread(Start);
            socketThread.Start();
        }

        public void Send(string request)
        {
            if (State != ConnectionStateEnum.Connected)
                throw new Exception("Invalid State - Not Connected");

            if (string.IsNullOrEmpty(request))
            {
                throw new ArgumentException("request must be a non empty string", "request");
            }

            byte[] data = Encoding.ASCII.GetBytes(request + Delimiter);
            NetworkStream ns = m_tcpClient.GetStream();
            if (!ns.CanWrite)
            {
                Disconnected();
                throw new Exception("Disconnected");
            }
            
            ns.Write(data, 0, data.Length);
        }

        public void Disconnect()
        {
            throw new NotImplementedException();
        }

        protected virtual void RaiseOnConneced(EventArgs args)
        {
            State = ConnectionStateEnum.Connected;
            if (OnConnect != null)
                OnConnect(this, args);
        }

        protected virtual void RaiseOnReconnect(int args)
        {
            State = ConnectionStateEnum.Connecting;
            if (OnReconnect != null)
                OnReconnect(this, args);
        }

        protected virtual void RaiseOnDisconneced(EventArgs args)
        {
            State = ConnectionStateEnum.Disconnected;
            if (OnDisconnect != null)
                OnDisconnect(this, args);
        }

        protected virtual void RaiseOnMessage(string args)
        {
            if (OnMessage != null)
                OnMessage(this, args);
        }

        private void Start()
        {
            NetworkStream ns = null;
            string msg = null;
            for (int i = 0; i < Reconnects; ++i)
            {
                RaiseOnReconnect(i+1);
                try
                {
                    m_tcpClient = new TcpClient(m_host, m_port);
                    
                    // Do the handshake before raising the connect event
                    ns = m_tcpClient.GetStream();
                    msg = Handshake(ns);

                    // If msg is null it means handshake failed
                    if (msg == null)
                    {
                        if (m_tcpClient.Connected)
                            m_tcpClient.Close();
                        continue;
                    }

                    RaiseOnConneced(EventArgs.Empty);
                    break;
                }
                catch
                {
                }
            }

            if (State != ConnectionStateEnum.Connected)
            {
                RaiseOnDisconneced(EventArgs.Empty);
                return;
            }
            
            // Start the reader loop
            Reader(ns, msg);
        }

        private void Disconnected()
        {
            // Make sure we are closed
            if (m_tcpClient.Connected)
                m_tcpClient.Close();

            RaiseOnDisconneced(EventArgs.Empty);
        }

        private string Handshake(NetworkStream ns)
        {
            // Keep reading until the end of the handshake
            string msg = "";
            int i;
            while ((i = msg.IndexOf("\r\n")) < 0)
            {
                if (!ns.CanRead)
                    return null;

                byte[] data = new byte[1024];
                int bytesRead = ns.Read(data, 0, 1024);

                // If no bytes read consider it as a failed connect attempt
                if (bytesRead < 1)
                    return null;

                msg += Encoding.ASCII.GetString(data, 0, bytesRead);
            }

            // Get the handshake data and the remainder of the message (can be the beginning of the next message).
            string hs = msg.Substring(0, i);
            string remainder = msg.Length > (i+2) ? msg.Substring(i + 2) : "";

            string[] hsParts = hs.Split(' ');
            if (hsParts.Length < 2)
            {
                // Invalid handshake data
                return null;
            }

            SessionId = hsParts[0];
            Delimiter = hsParts[1];

            return remainder;
        }

        private void Reader(NetworkStream ns, string buffer)
        {
            // Keep reading until delimiter
            while (true)
            {
                int i = buffer.IndexOf(Delimiter);
                if (i >= 0)
                {
                    string message = buffer.Substring(0, i);
                    buffer = buffer.Length > (i + Delimiter.Length) ? buffer.Substring(i + Delimiter.Length) : "";
                    HandleMessage(message);
                }

                // If can't read - disconnected
                if (!ns.CanRead)
                {
                    Disconnected();
                    return;
                }

                byte[] data = new byte[1024];
                int bytesRead = ns.Read(data, 0, 1024);

                // If no bytes read - disconnected
                if (bytesRead < 1)
                {
                    Disconnected();
                    return;
                }

                buffer += Encoding.ASCII.GetString(data, 0, bytesRead);
            }
        }

        private void HandleMessage(string msg)
        {
            RaiseOnMessage(msg);
        }

        #endregion

    }
}