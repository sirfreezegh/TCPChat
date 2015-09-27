﻿using Engine.API.ClientCommands;
using Engine.Helpers;
using Engine.Model.Entities;
using Engine.Model.Server;
using System;
using System.Linq;
using System.Security;

namespace Engine.API.ServerCommands
{
  [SecurityCritical]
  class ServerSendRoomMessageCommand :
    BaseServerCommand,
    ICommand<ServerCommandArgs>
  {
    public const ushort CommandId = (ushort)ServerCommand.SendRoomMessage;

    public ushort Id
    {
      [SecuritySafeCritical]
      get { return CommandId; }
    }

    [SecuritySafeCritical]
    public void Run(ServerCommandArgs args)
    {
      var receivedContent = Serializer.Deserialize<MessageContent>(args.Message);

      if (string.IsNullOrEmpty(receivedContent.Message))
        throw new ArgumentException("Message");

      if (string.IsNullOrEmpty(receivedContent.RoomName))
        throw new ArgumentException("RoomName");

      if (!RoomExists(receivedContent.RoomName, args.ConnectionId))
        return;

      using (var server = ServerModel.Get())
      {
        var room = server.Rooms[receivedContent.RoomName];

        if (!room.Users.Contains(args.ConnectionId))
        {
          ServerModel.API.SendSystemMessage(args.ConnectionId, "Вы не можете отправить сообщение, т.к. не входите в состав этой комнаты.");
          return;
        }

        if (receivedContent.MessageId != null && !room.IsMessageBelongToUser(args.ConnectionId, receivedContent.MessageId.Value))
        {
          ServerModel.API.SendSystemMessage(args.ConnectionId, "Вы не можете редактировать это сообщение.");
          return;
        }

        Message message = null;
        if (receivedContent.MessageId == null)
          message = room.AddMessage(args.ConnectionId, receivedContent.Message);
        else
        {
          message = room.GetMessage(receivedContent.MessageId.Value);
          message.Text = receivedContent.Message;
        }

        var sendingContent = new ClientOutRoomMessageCommand.MessageContent
        {
          Message = message.Text,
          RoomName = receivedContent.RoomName,
          Sender = args.ConnectionId,
          MessageId = message.Id
        };

        foreach (string user in room.Users.Where(u => u != null))
          ServerModel.Server.SendMessage(user, ClientOutRoomMessageCommand.CommandId, sendingContent);
      }
    }

    [Serializable]
    public class MessageContent
    {
      private string message;
      private string roomName;
      private long? messageId;

      public string Message
      {
        get { return message; }
        set { message = value; }
      }

      public string RoomName
      {
        get { return roomName; }
        set { roomName = value; }
      }

      public long? MessageId
      {
        get { return messageId; }
        set { messageId = value; }
      }
    }
  }
}
