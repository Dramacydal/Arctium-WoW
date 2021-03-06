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
using System.Net;
using AuthServer.Managers;
using AuthServer.Network;
using Framework.Attributes;
using Framework.Constants.Misc;
using Framework.Constants.Net;
using Framework.Logging;
using Framework.Misc;
using Framework.Network.Packets;

namespace AuthServer.Packets.Handlers
{
    class RealmHandler
    {
        [AuthMessage(AuthClientMessage.JoinRequest, AuthChannel.WoW)]
        public static void OnJoinRequest(AuthPacket packet, AuthSession session)
        {
            var clientSalt = BitConverter.GetBytes(packet.Read<uint>(32));
            var serverSalt = new byte[0].GenerateRandomKey(4);

            session.GenerateSessionKey(clientSalt, serverSalt);

            // Continue if sessionKey is not empty
            if (session.Account.SessionKey != "")
            {
                var joinResponse = new AuthPacket(AuthServerMessage.JoinResponse, AuthChannel.WoW);

                joinResponse.Write(Manager.Realms.RealmList.Count == 0, 1);

                joinResponse.Write(BitConverter.ToUInt32(serverSalt, 0), 32);
                joinResponse.Write(Manager.Realms.RealmList.Count * 2, 5);

                foreach (var realm in Manager.Realms.RealmList)
                {
                    var ip = IPAddress.Parse(realm.Value.IP).GetAddressBytes();
                    var port = BitConverter.GetBytes(realm.Value.Port);

                    Array.Reverse(port);

                    joinResponse.Write(ip);
                    joinResponse.Write(port);
                    joinResponse.Write(ip);
                    joinResponse.Write(port);
                }

                joinResponse.Flush();
                joinResponse.Write(0, 5);

                session.Send(joinResponse);
            }
        }

        [AuthMessage(AuthClientMessage.RealmUpdate, AuthChannel.WoW)]
        public static void OnRealmUpdate(AuthPacket packet, AuthSession session)
        {
            Log.Message(LogType.Debug, "Received realm update.");

            var complete = new AuthPacket(AuthServerMessage.Complete, AuthChannel.WoW);

            complete.Flush();
            complete.Write(0, 8);

            var realmCounter = 0;

            foreach (var realm in Manager.Realms.RealmList)
            {
                var realmlist = new AuthPacket(AuthServerMessage.RealmUpdate, AuthChannel.WoW);

                realmlist.Write(true, 1);
                realmlist.Write(1, 32);
                realmlist.Write(0f, 32);
                realmlist.Write(realm.Value.Flags, 8);
                realmlist.Write(realm.Value.Id, 19);
                realmlist.Write(0x80000000 + realm.Value.Type, 32);
                realmlist.WriteString(realm.Value.Name, 10, false);
                realmlist.Write(false, 1);
                realmlist.Write(realm.Value.Status, 8);
                realmlist.Write(0, 12);
                realmlist.Write(0, 8);
                realmlist.Write(0, 32);
                realmlist.Write(++realmCounter, 8);

                // End
                realmlist.Flush();
                realmlist.Write(new byte[] { 0x43, 0x02 });
                realmlist.Finish();

                complete.Write(realmlist.Data);
            }

            session.Send(complete);
        }
    }
}
