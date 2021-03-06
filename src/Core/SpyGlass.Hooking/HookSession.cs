﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SpyGlass.Hooking.Protocol;

namespace SpyGlass.Hooking
{
    public class HookSession
    {
        private readonly SynchronizationContext _context;
        public event EventHandler<Message> MessageReceived;
        public event EventHandler<Message> MessageSent;
        public event EventHandler<HookEventArgs> HookTriggered;
        
        private readonly Socket _socket;
        private readonly byte[] _header = new byte[3 * sizeof(int)];
        private readonly byte[] _buffer = new byte[1024];
        
        private readonly BlockingCollection<Message> _bufferedMessages = new BlockingCollection<Message>();

        private int _sequenceNumber = 0;
        
        public const int Timeout = 10000;

        public HookSession(IHookParametersDetector detector)
        {
            _context = new SynchronizationContext();
            Detector = detector;

            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        }

        public IHookParametersDetector Detector
        {
            get;
        }

        public void Connect(EndPoint endPoint)
        {
            _socket.Connect(endPoint);
            new Thread(ReceiveLoop)
            {
                IsBackground = true
            }.Start();
        }

        public void Set(IntPtr address)
        {
            var parameters = Detector.Detect(this, address);

            Send(new SetHookMessage(address, parameters.BytesToOverwrite, parameters.Fixups));
            WaitForAcknowledgement();
        }

        public void Unset(IntPtr address)
        {
            Send(new UnsetHookMessage(address));
            WaitForAcknowledgement();
        }

        public byte[] ReadMemory(IntPtr address, int length)
        {
            Send(new MemoryReadRequest(address, length));
            var response = WaitForResponse<MemoryReadResponse>();
            return response.Data;
        }

        public void WriteMemory(IntPtr address, byte[] data)
        {
            Send(new MemoryEditRequest(address, data));
            WaitForAcknowledgement();
        }

        public IntPtr GetProcAddress(string moduleName, string procedureName)
        {
            Send(new ProcAddressRequest(moduleName, procedureName));
            var response = WaitForResponse<ProcAddressResponse>();
            return response.Address;
        }

        private void Send(Message message)
        {
            int number = Interlocked.Increment(ref _sequenceNumber);
            message.SequenceNumber = number;
            
            OnMessageSent(message);
            _socket.Send(MessageEncoder.EncodeMessage(message));
        }

        private void ReceiveLoop()
        {
            while (true)
            {
                var message = ReceiveNextMessage();
                OnMessageReceived(message);
                
                switch (message)
                {
                    case CallbackMessage callback:
                        _context.Post(_ =>
                        {
                            var oldValues = callback.Registers.ToArray();
                            var eventArgs = new HookEventArgs(callback.Registers);
                            OnHookTriggered(eventArgs);
                            
                            var continueMessage = new ContinueMessage(callback.Id);
                            for (int i = 0; i < callback.Registers.Count; i++)
                            {
                                if (oldValues[i] != callback.Registers[i])
                                    continueMessage.RegisterChanges[i] = callback.Registers[i];
                            }

                            Send(continueMessage);
                            WaitForAcknowledgement();
                        }, null);
                        break;
                    
                    default:
                        _bufferedMessages.Add(message);
                        break;
                }
            }
        }

        private Message ReceiveNextMessage()
        {
            using (var stream = new MemoryStream())
            {
                _socket.Receive(_header, _header.Length, SocketFlags.None);
                stream.Write(_header, 0, _header.Length);
                
                int length = BitConverter.ToInt32(_header, 0);
                while (stream.Length < length + _header.Length)
                {
                    int received = _socket.Receive(_buffer, 0, _buffer.Length, SocketFlags.None);
                    stream.Write(_buffer, 0, received);
                }

                return MessageEncoder.DecodeMessage(stream.ToArray());
            }
        }

        private TMessage WaitForResponse<TMessage>()
            where TMessage : Message
        {
            if (!_bufferedMessages.TryTake(out var message, Timeout))
                throw new InvalidOperationException("Request timed out.");
            
            if (message is TMessage m)
                return m;
            
            throw new InvalidOperationException(
                $"Server responded with an unexpected {message.GetType()} message.");
        }

        private void WaitForAcknowledgement()
        {
            var response = WaitForResponse<ActionCompletedMessage>();
            
            if (response.ErrorCode != HookErrorCode.Success)
                throw new InvalidOperationException($"Server responded with error code {response.ErrorCode}.");
        }

        protected virtual void OnMessageReceived(Message e)
        {
            MessageReceived?.Invoke(this, e);
        }

        protected virtual void OnMessageSent(Message e)
        {
            MessageSent?.Invoke(this, e);
        }

        protected virtual void OnHookTriggered(HookEventArgs e)
        {
            HookTriggered?.Invoke(this, e);
        }
    }
    
}