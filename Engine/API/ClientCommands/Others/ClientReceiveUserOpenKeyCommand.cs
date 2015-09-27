﻿using Engine.API.ServerCommands;
using Engine.Helpers;
using Engine.Model.Client;
using Engine.Network;
using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Engine.API.ClientCommands
{
  [SecurityCritical]
  class ClientReceiveUserOpenKeyCommand :
    ICommand<ClientCommandArgs>
  {
    public const ushort CommandId = (ushort)ClientCommand.ReceiveUserOpenKey;

    public ushort Id
    {
      [SecuritySafeCritical]
      get { return CommandId; }
    }

    [SecuritySafeCritical]
    public void Run(ClientCommandArgs args)
    {
      var API = ClientModel.API as StandardClientAPI;
      if (API == null)
        throw new InvalidOperationException("ClientReceiveUserOpenKeyCommand need StandardAPI");

      var receivedContent = Serializer.Deserialize<MessageContent>(args.Message);

      if (string.IsNullOrEmpty(receivedContent.Nick))
        throw new ArgumentException("Nick");

      var waitingMessage = API.GetWaitingMessage(receivedContent.Nick);
      if (waitingMessage == null)
        return;

      var sendingContent = new ServerSendPrivateMessageCommand.MessageContent { Receiver = receivedContent.Nick };
      var provider = new AesCryptoServiceProvider
      {
        Padding = PaddingMode.Zeros,
        Mode = CipherMode.CBC
      };

      using (var messageCrypter = new Crypter(provider))
      {
        var symmetricKey = messageCrypter.GenerateKey();

        using (var keyCryptor = new RSACryptoServiceProvider(AsyncClient.CryptorKeySize))
        {
          keyCryptor.ImportParameters(receivedContent.OpenKey);
          sendingContent.Key = keyCryptor.Encrypt(symmetricKey, true);
        }

        using (MemoryStream encryptedMessageStream = new MemoryStream(),
               messageStream = new MemoryStream(Encoding.Unicode.GetBytes(waitingMessage.Message)))
        {
          messageCrypter.EncryptStream(messageStream, encryptedMessageStream);
          sendingContent.Message = encryptedMessageStream.ToArray();
        }
      }

      ClientModel.Client.SendMessage(ServerSendPrivateMessageCommand.CommandId, sendingContent);
    }

    [Serializable]
    public class MessageContent
    {
      private string nick;
      private RSAParameters openKey;

      public string Nick
      {
        get { return nick; }
        set { nick = value; }
      }

      public RSAParameters OpenKey
      {
        get { return openKey; }
        set { openKey = value; }
      }
    }
  }
}
