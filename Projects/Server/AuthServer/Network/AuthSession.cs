﻿/*
 * Copyright (C) 2012-2014 Arctium Emulation <http://arctium.org>
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AuthServer.Packets;
using Framework.Constants.Misc;
using Framework.Constants.Net;
using Framework.Cryptography.BNet;
using Framework.Database;
using Framework.Database.Auth.Entities;
using Framework.Logging;
using Framework.Misc;
using Framework.Network.Packets;

namespace AuthServer.Network
{
    class AuthSession : IDisposable
    {
        public Account Account { get; set; }
        public List<Module> Modules { get; set; }
        public SRP6a SecureRemotePassword { get; set; }
        public BNetCrypt Crypt { get; set; }

        Socket client;
        byte[] dataBuffer = new byte[0x800];

        public AuthSession(Socket socket)
        {
            client = socket;
        }

        public void Accept()
        {
            var socketEventargs = new SocketAsyncEventArgs();

            socketEventargs.SetBuffer(dataBuffer, 0, dataBuffer.Length);

            socketEventargs.Completed += Process;
            socketEventargs.UserToken = client;
            socketEventargs.SocketFlags = SocketFlags.None;

            client.ReceiveAsync(socketEventargs);
        }

        void Process(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                var socket = e.UserToken as Socket;
                var recievedBytes = e.BytesTransferred;

                if (recievedBytes != 0)
                {
                    if (Crypt == null && dataBuffer[0] == 0x45 && dataBuffer[1] == 0x01)
                    {
                        Crypt = new BNetCrypt(SecureRemotePassword.SessionKey);

                        Buffer.BlockCopy(dataBuffer, 2, dataBuffer, 0, recievedBytes -= 2);

                        Log.Message(LogType.Debug, "Encryption for account '{0}' enabled", Account.Id);
                    }

                    if (Crypt != null && Crypt.IsInitialized)
                        Crypt.Decrypt(dataBuffer, recievedBytes);

                    ProcessPacket(recievedBytes);

                    client.ReceiveAsync(e);
                }
                else
                    socket.Close();
            }
            catch (Exception ex)
            {
                Log.Message(LogType.Error, "{0}", ex.Message);
            }
        }

        void ProcessPacket(int size)
        {
            var packet = new AuthPacket(dataBuffer, size);
            
            PacketLog.Write<AuthClientMessage>(packet.Header.Message, packet.Data, client.RemoteEndPoint, packet.Header.Channel);
           
            if (packet != null)
                PacketManager.InvokeHandler(packet, this);
        }

        public void Send(AuthPacket packet)
        {
            try
            {
                packet.Finish();

                if (packet.Header != null)
                    PacketLog.Write<AuthServerMessage>(packet.Header.Message, packet.Data, client.RemoteEndPoint);

                if (Crypt != null && Crypt.IsInitialized)
                    Crypt.Encrypt(packet.Data, packet.Data.Length);

                var socketEventargs = new SocketAsyncEventArgs();

                socketEventargs.SetBuffer(packet.Data, 0, packet.Data.Length);

                socketEventargs.Completed += SendCompleted;
                socketEventargs.UserToken = packet;
                socketEventargs.RemoteEndPoint = client.RemoteEndPoint;
                socketEventargs.SocketFlags = SocketFlags.None;

                client.Send(packet.Data);
            }
            catch (SocketException ex)
            {
                Log.Message(LogType.Error, "{0}", ex.Message);

                client.Close();
            }
        }

        void SendCompleted(object sender, SocketAsyncEventArgs e)
        {
        }

        public void GenerateSessionKey(byte[] clientSalt, byte[] serverSalt)
        {
            var hmac = new HMACSHA1(SecureRemotePassword.SessionKey);
            var wow = Encoding.ASCII.GetBytes("WoW\0");
            var wowSessionKey = new byte[0x28];

            hmac.TransformBlock(wow, 0, wow.Length, wow, 0);
            hmac.TransformBlock(clientSalt, 0, clientSalt.Length, clientSalt, 0);
            hmac.TransformFinalBlock(serverSalt, 0, serverSalt.Length);

            Buffer.BlockCopy(hmac.Hash, 0, wowSessionKey, 0, hmac.Hash.Length);

            hmac.Initialize();
            hmac.TransformBlock(wow, 0, wow.Length, wow, 0);
            hmac.TransformBlock(serverSalt, 0, serverSalt.Length, serverSalt, 0);
            hmac.TransformFinalBlock(clientSalt, 0, clientSalt.Length);

            Buffer.BlockCopy(hmac.Hash, 0, wowSessionKey, hmac.Hash.Length, hmac.Hash.Length);

            Account.SessionKey = wowSessionKey.ToHexString();

            // Update SessionKey in database
            DB.Auth.Update(Account, "SessionKey", Account.SessionKey);
        }

        public void Dispose()
        {
            client = null;
            Account = null;
            SecureRemotePassword = null;
        }
    }
}
