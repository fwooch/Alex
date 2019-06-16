﻿using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Alex.API.Data;
using Alex.API.Network;
using Alex.API.Utils;
using Alex.API.World;
using Alex.Gamestates;
using Jose;
using Microsoft.Xna.Framework;
using MiNET;
using MiNET.Blocks;
using MiNET.Client;
using MiNET.Net;
using MiNET.Utils;
using NLog;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.X509;
using BlockCoordinates = Alex.API.Utils.BlockCoordinates;
using NewtonsoftMapper = MiNET.NewtonsoftMapper;
using Skin = MiNET.Utils.Skins.Skin;

namespace Alex.Worlds.Bedrock
{
	public class BedrockMotd
	{
		public string Edition;
		public string MOTD;
		public int MaxPlayers;
		public int Players;
		public int ProtocolVersion;
		public string ClientVersion;

		public BedrockMotd(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw)) return;

			var split = raw.Split(';');
			int i = 0;
			Edition = split[i++];
			MOTD = split[i++];

			if (int.TryParse(split[i++], out int protocolVersion))
			{
				ProtocolVersion = protocolVersion;
			}
			
			ClientVersion = split[i++];

			if (int.TryParse(split[i++], out int players))
			{
				Players = players;
			}

			if (int.TryParse(split[i++], out int maxplayers))
			{
				MaxPlayers = maxplayers;
			}
		}
	}
	public class BedrockClient : MiNetClient, INetworkProvider, IChatProvider, IDisposable
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(BedrockClient));
		
		public ManualResetEventSlim ConnectionAcceptedWaitHandle { get; }
		public BedrockWorldProvider WorldProvider { get; }
		public EventHandler<BedrockMotd> OnMotdReceivedHandler;
		public BedrockMotd KnownMotd = new BedrockMotd(string.Empty);

        private Alex Alex { get; }
		public BedrockClient(Alex alex,IPEndPoint endpoint, string username, DedicatedThreadPool threadPool, BedrockWorldProvider wp) : base(endpoint,
			username, threadPool)
        {
            Alex = alex;
			WorldProvider = wp;
			ConnectionAcceptedWaitHandle = new ManualResetEventSlim(false);
			MessageDispatcher = new McpeClientMessageDispatcher(new BedrockClientPacketHandler(this, alex));
			IsEmulator = true;
			CurrentLocation = new MiNET.Utils.PlayerLocation(0,0,0);

			base.ChunkRadius = alex.GameSettings.RenderDistance;
		}

        public void ShowDisconnect(string reason, bool useTranslation = false)
        {
            if (Alex.GameStateManager.GetActiveState() is DisconnectedScreen s)
            {
                if (useTranslation)
                {
                    s.DisconnectedTextElement.TranslationKey = reason;
                }
                else
                {
                    s.DisconnectedTextElement.Text = reason;
                }

                return;
            }

            s = new DisconnectedScreen();
            if (useTranslation)
            {
                s.DisconnectedTextElement.TranslationKey = reason;
            }
            else
            {
                s.DisconnectedTextElement.Text = reason;
            }

            Alex.GameStateManager.SetActiveState(s, false);
            Alex.GameStateManager.RemoveState("play");
            Dispose();
        }

        public override void OnConnectionRequestAccepted()
		{
			ConnectionAcceptedWaitHandle.Set();

            Thread.Sleep(50);
            SendNewIncomingConnection();
            //_connectedPingTimer = new Timer(state => SendConnectedPing(), null, 1000, 1000);
            Thread.Sleep(50);
            SendAlexLogin(Username);
        }

        private void SendAlexLogin(string username)
        {
            JWT.JsonMapper = new NewtonsoftMapper();

            var clientKey = CryptoUtils.GenerateClientKey();

            ECDsa signKey = ConvertToSingKeyFormat(clientKey);

            byte[] data = CryptoUtils.CompressJwtBytes(EncodeJwt(username, clientKey, signKey, IsEmulator), EncodeSkinJwt(clientKey, signKey, username), CompressionLevel.Fastest);

            McpeLogin loginPacket = new McpeLogin
            {
                protocolVersion = McpeProtocolInfo.ProtocolVersion,
                payload = data
            };

            Session.CryptoContext = new CryptoContext()
            {
                ClientKey = clientKey,
                UseEncryption = false,
            };

            SendPacket(loginPacket);
        }

        private static ECDsa ConvertToSingKeyFormat(AsymmetricCipherKeyPair key)
        {
            ECPublicKeyParameters pubAsyKey = (ECPublicKeyParameters)key.Public;
            ECPrivateKeyParameters privAsyKey = (ECPrivateKeyParameters)key.Private;

            var signParam = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP384,
                Q =
                {
                    X = pubAsyKey.Q.AffineXCoord.GetEncoded(),
                    Y = pubAsyKey.Q.AffineYCoord.GetEncoded()
                }
            };
            signParam.D = CryptoUtils.FixDSize(privAsyKey.D.ToByteArrayUnsigned(), signParam.Q.X.Length);
            signParam.Validate();

            return ECDsa.Create(signParam);
        }

        private string b64Key;
        private byte[] EncodeJwt(string username, AsymmetricCipherKeyPair newKey, ECDsa signKey, bool isEmulator)
        {
            long iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long exp = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

            //ECDsa signKey = ConvertToSingKeyFormat(newKey);
            b64Key = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(newKey.Public).GetEncoded().EncodeBase64();

            CertificateData certificateData = new CertificateData
            {
                Exp = exp,
                Iat = iat,
                ExtraData = new ExtraData
                {
                    DisplayName = username,
                    Identity = Guid.NewGuid().ToString(),
                    XUID = ""
                },
                Iss = "self",
                IdentityPublicKey = b64Key,
                CertificateAuthority = true,
                Nbf = iat,
                RandomNonce = new Random().Next(),
            };

            string val = JWT.Encode(certificateData, signKey, JwsAlgorithm.ES384, new Dictionary<string, object> { { "x5u", b64Key } }, new JwtSettings()
            {
                JsonMapper = new JWTMapper()
            });

            Log.Warn(JWT.Payload(val));

            Log.Warn(string.Join(";", JWT.Headers(val)));

            val = $@"{{ ""chain"": [""{val}""] }}";

            return Encoding.UTF8.GetBytes(val);
        }

        private byte[] EncodeSkinJwt(AsymmetricCipherKeyPair newKey, ECDsa signKey, string username)
        {
            MiNET.Utils.Skins.Skin skin = new Skin
            {
                Slim = false,
                SkinData = Encoding.Default.GetBytes(new string('Z', 8192)),
                SkinId = "Standard_Custom",
                CapeData = new byte[0],
                SkinGeometryName = "geometry.humanoid.custom",
                SkinGeometry = ""
            };

            string skin64 = Convert.ToBase64String(skin.SkinData);
            string cape64 = Convert.ToBase64String(skin.CapeData);

            string skinData = $@"
{{
	""CapeData"": """",
	""ADRole"": 0,
	""ClientRandomId"": {new Random().Next()},
	""CurrentInputMode"": 1,
	""DefaultInputMode"": 1,
	""DeviceModel"": ""Alex"",
	""DeviceOS"": 7,
	""GameVersion"": ""{McpeProtocolInfo.GameVersion}"",
	""IsEduMode"": {Config.GetProperty("EnableEdu", false).ToString().ToLower()},
	""GuiScale"": 0,
	""LanguageCode"": ""en_US"",
	""PlatformOfflineId"": """",
	""PlatformOnlineId"": """",
	""SelfSignedId"": ""{Guid.NewGuid().ToString()}"",
	""ServerAddress"": ""{base.ServerEndpoint.Address.ToString()}:{base.ServerEndpoint.Port.ToString()}"",
	""SkinData"": ""{skin64}"",
	""SkinId"": ""{skin.SkinId}"",
    ""SkinGeometryName"": ""{skin.SkinGeometryName}"",
    ""SkinGeometry"": ""{skin.SkinGeometry}"",
    ""CapeData"": ""{cape64}"",
	""TenantId"": ""38dd6634-1031-4c50-a9b4-d16cd9d97d57"",
	""ThirdPartyName"": ""{username}"",
	""UIProfile"": 0,
	""IsAlex"": 1
}}";

         //  ECDsa signKey = ConvertToSingKeyFormat(newKey);
           // string b64Key = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(newKey.Public).GetEncoded().EncodeBase64();

            string val = JWT.Encode(skinData, signKey, JwsAlgorithm.ES384, new Dictionary<string, object> { { "x5u", b64Key } }, new JwtSettings()
            {
                JsonMapper = new JWTMapper()
            });

          //  Log.Warn(JWT.Payload(val));

            return Encoding.UTF8.GetBytes(val);
        }

        public bool IgnoreUnConnectedPong = false;
		protected override void OnUnconnectedPong(UnconnectedPong packet, IPEndPoint senderEndpoint)
		{
			KnownMotd = new BedrockMotd(packet.serverName);
			OnMotdReceivedHandler?.Invoke(this, KnownMotd);
			if (IgnoreUnConnectedPong) return;

			base.OnUnconnectedPong(packet, senderEndpoint);
		}


        public bool IsConnected => base.HaveServer;
		public IWorldReceiver WorldReceiver { get; set; }

		void INetworkProvider.EntityAction(int entityId, EntityAction action)
		{
			PlayerAction translated;
			switch (action)
			{
				case EntityAction.StartSneaking:
					translated = PlayerAction.StartSneak;
					break;
				case EntityAction.StopSneaking:
					translated = PlayerAction.StopSneak;
					break;

				case EntityAction.StartSprinting:
					translated = PlayerAction.StartSprint;
					break;
				case EntityAction.StopSprinting:
					translated = PlayerAction.StopSprint;
					break;

				default:
					return;
			}
			
			SendPlayerAction(translated, null, null);
		}

		void INetworkProvider.SendChatMessage(string message)
		{
			SendChat(message);
		}

		public void SendPlayerAction(PlayerAction action, BlockCoordinates? coordinates, int? blockFace )
		{
			McpePlayerAction packet = McpePlayerAction.CreateObject();
			packet.actionId = (int) action;
			
			if (coordinates.HasValue)
				packet.coordinates = new MiNET.Utils.BlockCoordinates(coordinates.Value.X, 
					coordinates.Value.Y, coordinates.Value.Z);

			if (blockFace.HasValue)
				packet.face = blockFace.Value;
			
			SendPacket(packet);
		}
		
	    public void PlayerDigging(DiggingStatus status, BlockCoordinates position, BlockFace face)
	    {
		    if (status == DiggingStatus.Started)
		    {
			    SendPlayerAction(PlayerAction.StartBreak, position, (int)face);
		    }
		    else if (status == DiggingStatus.Finished)
		    {
			    SendPlayerAction(PlayerAction.StopBreak, position, (int)face);
		    }
		    else if (status == DiggingStatus.Cancelled)
		    {
			    SendPlayerAction(PlayerAction.AbortBreak, position, (int)face);
		    }
	    }

	    public void BlockPlaced(BlockCoordinates position, BlockFace face, int hand, Vector3 cursorPosition)
	    {
		    Log.Warn("TODO: Implement Block Placement");
	    }

	    public void UseItem(int hand)
		{
			Log.Warn("TODO: Implement UseItem");
		}

		public void HeldItemChanged(short slot)
		{
			Log.Warn("TODO: Implement Held Item Changed");
		}

		public void Close()
		{
			base.StopClient();
		}

		void IChatProvider.Send(string message)
		{
			SendChat(message);
		}

		void IChatProvider.RequestTabComplete(string text, out int transactionId)
		{
			transactionId = 0;
		}

		public void ChunkReceived(ChunkColumn chunkColumn)
		{
			WorldProvider.ChunkReceived(chunkColumn);
		}

		public void RequestChunkRadius(int radius)
		{
			var packet = McpeRequestChunkRadius.CreateObject();
			packet.chunkRadius = radius;

			base.SendPacket(packet);
		}

		
		public void Dispose()
		{
			StopClient();
		}
	}
}
