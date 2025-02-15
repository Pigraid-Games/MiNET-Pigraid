﻿#region LICENSE

// The contents of this file are subject to the Common Public Attribution
// License Version 1.0. (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// https://github.com/NiclasOlofsson/MiNET/blob/master/LICENSE.
// The License is based on the Mozilla Public License Version 1.1, but Sections 14
// and 15 have been added to cover use of software over a computer network and
// provide for limited attribution for the Original Developer. In addition, Exhibit A has
// been modified to be consistent with Exhibit B.
// 
// Software distributed under the License is distributed on an "AS IS" basis,
// WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
// the specific language governing rights and limitations under the License.
// 
// The Original Code is MiNET.
// 
// The Original Developer is the Initial Developer.  The Initial Developer of
// the Original Code is Niclas Olofsson.
// 
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2020 Niclas Olofsson.
// All Rights Reserved.

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using Jose;
using log4net;
using MiNET.Net;
using MiNET.Utils;
using MiNET.Utils.Cryptography;
using MiNET.Utils.Metadata;
using MiNET.Utils.Vectors;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using static MiNET.Entities.Entity;

namespace MiNET.Client
{
	public abstract class McpeClientMessageHandlerBase : IMcpeClientMessageHandler
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof(McpeClientMessageHandlerBase));

		public MiNetClient Client { get; }

		public McpeClientMessageHandlerBase(MiNetClient client)
		{
			Client = client;
		}

		public virtual void HandleMcpePlayStatus(McpePlayStatus message)
		{
			Client.PlayerStatus = message.status;

			if (Client.PlayerStatus == 3)
			{
				Client.HasSpawned = true;
				//if (Client.IsEmulator)
				{
					Client.PlayerStatusChangedWaitHandle.Set();
					//Client.SendMcpeMovePlayer();
				}
			}
		}

		public virtual void HandleMcpeServerToClientHandshake(McpeServerToClientHandshake message)
		{
			string token = message.token;
			if (Log.IsDebugEnabled) Log.Debug($"JWT:\n{token}");

			IDictionary<string, dynamic> headers = JWT.Headers(token);
			string x5u = headers["x5u"];

			if (Log.IsDebugEnabled) Log.Debug($"JWT payload:\n{JWT.Payload(token)}");

			var remotePublicKey = (ECPublicKeyParameters) PublicKeyFactory.CreateKey(x5u.DecodeBase64());

			var signParam = new ECParameters
			{
				Curve = ECCurve.NamedCurves.nistP384,
				Q =
				{
					X = remotePublicKey.Q.AffineXCoord.GetEncoded(),
					Y = remotePublicKey.Q.AffineYCoord.GetEncoded()
				},
			};
			signParam.Validate();

			var signKey = ECDsa.Create(signParam);

			try
			{
				var data = JWT.Decode<HandshakeData>(token, signKey);

				Client.InitiateEncryption(Base64Url.Decode(x5u), Base64Url.Decode(data.salt));
			}
			catch (Exception e)
			{
				Log.Error(token, e);
				throw;
			}
		}

		public virtual void HandleMcpeDisconnect(McpeDisconnect message)
		{
			Client.StopClient();
		}

		public virtual void HandleMcpeResourcePacksInfo(McpeResourcePacksInfo message)
		{
			//McpeResourcePackClientResponse response = new McpeResourcePackClientResponse();
			//response.responseStatus = 3;
			//SendPackage(response);

			if (message.texturepacks.Count != 0)
			{
				var resourcePackIds = new ResourcePackIds();

				foreach (ResourcePackInfo packInfo in message.texturepacks)
				{
					resourcePackIds.Add(packInfo.UUID.ToString());
				}

				var response = new McpeResourcePackClientResponse();
				response.responseStatus = 2;
				response.resourcepackids = resourcePackIds;
				Client.SendPacket(response);
			}
			else
			{
				var response = new McpeResourcePackClientResponse();
				response.responseStatus = 3;
				Client.SendPacket(response);
			}
		}

		public virtual void HandleMcpeResourcePackStack(McpeResourcePackStack message)
		{
			//if (message.resourcepackidversions.Count != 0)
			//{
			//	McpeResourcePackClientResponse response = new McpeResourcePackClientResponse();
			//	response.responseStatus = 2;
			//	response.resourcepackidversions = message.resourcepackidversions;
			//	SendPackage(response);
			//}
			//else
			{
				var response = new McpeResourcePackClientResponse();
				response.responseStatus = 4;
				Client.SendPacket(response);
			}
		}

		public virtual void HandleMcpeText(McpeText message)
		{
		}

		public virtual void HandleMcpeSetTime(McpeSetTime message)
		{
		}

		public virtual void HandleMcpeStartGame(McpeStartGame message)
		{
			var client = Client;
			client.EntityId = message.runtimeEntityId;
			client.NetworkEntityId = message.entityIdSelf;
			client.SpawnPoint = message.spawn;
			client.CurrentLocation = new PlayerLocation(client.SpawnPoint, message.rotation.X, message.rotation.X, message.rotation.Y);

			BlockPalette blockPalette = message.blockPalette;
			client.BlockPalette = blockPalette;
			client.LevelInfo.LevelName = message.worldName;
			client.LevelInfo.Version = 19133;
			client.LevelInfo.GameType = message.levelSettings.gamemode;

			var packet = McpeRequestChunkRadius.CreateObject();
			client.ChunkRadius = 5;
			packet.chunkRadius = client.ChunkRadius;

			if (Client.IsEmulator)
			{
				Client.HasSpawned = true;
				Client.PlayerStatusChangedWaitHandle.Set();
				Client.SendMcpeMovePlayer();
			}

			client.SendPacket(packet);
		}

		public virtual void HandleMcpeAddPlayer(McpeAddPlayer message)
		{
		}

		public virtual void HandleMcpeAddEntity(McpeAddEntity message)
		{
		}

		public virtual void HandleMcpeRemoveEntity(McpeRemoveEntity message)
		{
		}

		public virtual void HandleMcpeAddItemEntity(McpeAddItemEntity message)
		{
		}

		public virtual void HandleMcpeTakeItemActor(McpeTakeItemActor message)
		{
		}

		public virtual void HandleMcpeMoveEntity(McpeMoveEntity message)
		{
		}

		public virtual void HandleMcpeMovePlayer(McpeMovePlayer message)
		{
			if (message.runtimeEntityId != Client.EntityId) return;

			Client.CurrentLocation = new PlayerLocation(message.x, message.y, message.z);

			//Client.LevelInfo.SpawnX = (int) message.x;
			//Client.LevelInfo.SpawnY = (int) message.y;
			//Client.LevelInfo.SpawnZ = (int) message.z;

			//Client.SendMcpeMovePlayer();
		}

		public virtual void HandleMcpeRiderJump(McpeRiderJump message)
		{
		}

		public virtual void HandleMcpeUpdateBlock(McpeUpdateBlock message)
		{
		}

		public virtual void HandleMcpeAddPainting(McpeAddPainting message)
		{
		}

		public virtual void HandleMcpeLevelSoundEventOld(McpeLevelSoundEventOld message)
		{
		}

		public virtual void HandleMcpeLevelEvent(McpeLevelEvent message)
		{
		}

		public virtual void HandleMcpeBlockEvent(McpeBlockEvent message)
		{
		}

		public virtual void HandleMcpeEntityEvent(McpeEntityEvent message)
		{
			Log.Warn($"got entity event: {message.runtimeEntityId} / {message.eventId} / {message.data}");

		}

		public virtual void HandleMcpeMobEffect(McpeMobEffect message)
		{
			Log.Warn($"I got effect: {message.runtimeEntityId} / {message.eventId} / {message.effectId} / {message.amplifier} / {message.particles} / {message.duration} / {message.tick}");
		}

		public virtual void HandleMcpeUpdateAttributes(McpeUpdateAttributes message)
		{
		}

		public virtual void HandleMcpeInventoryTransaction(McpeInventoryTransaction message)
		{
		}

		public virtual void HandleMcpeMobEquipment(McpeMobEquipment message)
		{
			Log.Warn($"Entity with {message.runtimeEntityId} is holding {message.item}");
		}

		public virtual void HandleMcpeMobArmorEquipment(McpeMobArmorEquipment message)
		{
		}

		public virtual void HandleMcpeInteract(McpeInteract message)
		{
		}

		public virtual void HandleMcpeHurtArmor(McpeHurtArmor message)
		{
		}

		public virtual void HandleMcpeSetActorData(McpeSetActorData message)
		{
			//Log.Warn(JsonConvert.SerializeObject(message, Formatting.Indented));
			/*Log.Warn($"start =============================================");
			if (message.metadata[(int) MetadataFlags.EntityFlags] != null)
			{
				MetadataLong metadataLong = message.metadata[(int) MetadataFlags.EntityFlags] as MetadataLong;
				BitArray bits = new BitArray(BitConverter.GetBytes(metadataLong.Value));
				for (int i = 0; i < 64; i++)
				{
					DataFlags flag = (DataFlags) i;
					Log.Warn($"{flag}: {bits[i]}");
				}
			}

			if (message.metadata[(int) MetadataFlags.EntityFlags] != null)
			{
				MetadataLong metadataLong2 = message.metadata[(int) MetadataFlags.EntityFlags] as MetadataLong;
				BitArray bits2 = new BitArray(BitConverter.GetBytes(metadataLong2.Value));
				for (int i = 65; i < Enum.GetValues(typeof(DataFlags)).Length; i++)
				{
					DataFlags flag = (DataFlags) i;
					Log.Warn($"{flag}: {bits2[i - 64]}");
				}
			}
			Log.Warn($"end =============================================");
			*/
		}

		public virtual void HandleMcpeSetActorMotion(McpeSetActorMotion message)
		{
		}

		public virtual void HandleMcpeSetActorLink(McpeSetActorLink message)
		{
		}

		public virtual void HandleMcpeSetHealth(McpeSetHealth message)
		{
		}

		public virtual void HandleMcpeSetSpawnPosition(McpeSetSpawnPosition message)
		{
			Client.SpawnPoint = new Vector3(message.coordinates.X, message.coordinates.Y, message.coordinates.Z);
			Client.LevelInfo.SpawnX = (int) Client.SpawnPoint.X;
			Client.LevelInfo.SpawnY = (int) Client.SpawnPoint.Y;
			Client.LevelInfo.SpawnZ = (int) Client.SpawnPoint.Z;
		}

		public virtual void HandleMcpeAnimate(McpeAnimate message)
		{
		}

		public virtual void HandleMcpeRespawn(McpeRespawn message)
		{
			Client.CurrentLocation = new PlayerLocation(message.x, message.y, message.z);
		}

		public virtual void HandleMcpeContainerOpen(McpeContainerOpen message)
		{
		}

		public virtual void HandleMcpeContainerClose(McpeContainerClose message)
		{
		}

		public virtual void HandleMcpePlayerHotbar(McpePlayerHotbar message)
		{
		}

		public virtual void HandleMcpeInventoryContent(McpeInventoryContent message)
		{
		}

		public virtual void HandleMcpeInventorySlot(McpeInventorySlot message)
		{
		}

		public virtual void HandleMcpeContainerSetData(McpeContainerSetData message)
		{
		}

		public virtual void HandleMcpeCraftingData(McpeCraftingData message)
		{
		}

		public virtual void HandleMcpeCraftingEvent(McpeCraftingEvent message)
		{
		}

		public virtual void HandleMcpeGuiDataPickItem(McpeGuiDataPickItem message)
		{
		}

		public virtual void HandleMcpeAdventureSettings(McpeAdventureSettings message)
		{
			Client.UserPermission = (CommandPermission) message.commandPermission;
		}

		public virtual void HandleMcpeBlockEntityData(McpeBlockEntityData message)
		{
		}

		public virtual void HandleMcpeLevelChunk(McpeLevelChunk message)
		{
		}

		public virtual void HandleMcpeSetCommandsEnabled(McpeSetCommandsEnabled message)
		{
		}

		public virtual void HandleMcpeSetDifficulty(McpeSetDifficulty message)
		{
		}

		public virtual void HandleMcpeChangeDimension(McpeChangeDimension message)
		{
			Thread.Sleep(3000);

			var action = McpePlayerAction.CreateObject();
			action.runtimeEntityId = Client.EntityId;
			action.actionId = (int) PlayerAction.DimensionChangeAck;
			Client.SendPacket(action);
		}

		public virtual void HandleMcpeSetPlayerGameType(McpeSetPlayerGameType message)
		{
		}

		public virtual void HandleMcpePlayerList(McpePlayerList message)
		{
		}

		public virtual void HandleMcpeSimpleEvent(McpeSimpleEvent message)
		{
		}

		public virtual void HandleMcpeTelemetryEvent(McpeTelemetryEvent message)
		{
		}

		public virtual void HandleMcpeSpawnExperienceOrb(McpeSpawnExperienceOrb message)
		{
		}

		public virtual void HandleMcpeClientboundMapItemData(McpeClientboundMapItemData message)
		{
		}

		public virtual void HandleMcpeMapInfoRequest(McpeMapInfoRequest message)
		{
		}

		public virtual void HandleMcpeRequestChunkRadius(McpeRequestChunkRadius message)
		{
		}

		public virtual void HandleMcpeChunkRadiusUpdate(McpeChunkRadiusUpdate message)
		{
		}

		public virtual void HandleMcpeGameRulesChanged(McpeGameRulesChanged message)
		{
		}

		public virtual void HandleMcpeCamera(McpeCamera message)
		{
		}

		public virtual void HandleMcpeBossEvent(McpeBossEvent message)
		{
		}

		public virtual void HandleMcpeShowCredits(McpeShowCredits message)
		{
		}

		public virtual void HandleMcpeAvailableCommands(McpeAvailableCommands message)
		{
		}

		public virtual void HandleMcpeCommandOutput(McpeCommandOutput message)
		{
		}

		public virtual void HandleMcpeUpdateTrade(McpeUpdateTrade message)
		{
		}

		public virtual void HandleMcpeUpdateEquipment(McpeUpdateEquipment message)
		{
		}

		private Dictionary<string, uint> _resourcePackDataInfos = new Dictionary<string, uint>();

		public virtual void HandleMcpeResourcePackDataInfo(McpeResourcePackDataInfo message)
		{
			var request = new McpeResourcePackChunkRequest();
			request.packageId = message.packageId;
			request.chunkIndex = 0;
			Client.SendPacket(request);
			_resourcePackDataInfos.Add(message.packageId, message.chunkCount);
		}

		public virtual void HandleMcpeResourcePackChunkData(McpeResourcePackChunkData message)
		{
			if (message.chunkIndex + 1 < _resourcePackDataInfos[message.packageId])
			{
				var request = new McpeResourcePackChunkRequest();
				request.packageId = message.packageId;
				request.chunkIndex = message.chunkIndex + 1;
				Client.SendPacket(request);
			}
			else
			{
				_resourcePackDataInfos.Remove(message.packageId);
			}

			if (_resourcePackDataInfos.Count == 0)
			{
				var response = new McpeResourcePackClientResponse();
				response.responseStatus = 3;
				Client.SendPacket(response);
			}
		}

		public virtual void HandleMcpeTransfer(McpeTransfer message)
		{
		}

		public virtual void HandleMcpePlaySound(McpePlaySound message)
		{
		}

		public virtual void HandleMcpeStopSound(McpeStopSound message)
		{
		}

		public virtual void HandleMcpeSetTitle(McpeSetTitle message)
		{
		}

		public virtual void HandleMcpeAddBehaviorTree(McpeAddBehaviorTree message)
		{
		}

		public virtual void HandleMcpeStructureBlockUpdate(McpeStructureBlockUpdate message)
		{
		}

		public virtual void HandleMcpeShowStoreOffer(McpeShowStoreOffer message)
		{
		}

		public virtual void HandleMcpePlayerSkin(McpePlayerSkin message)
		{
		}

		public virtual void HandleMcpeSubClientLogin(McpeSubClientLogin message)
		{
		}

		public virtual void HandleMcpeInitiateWebSocketConnection(McpeInitiateWebSocketConnection message)
		{
		}

		public virtual void HandleMcpeSetLastHurtBy(McpeSetLastHurtBy message)
		{
		}

		public virtual void HandleMcpeBookEdit(McpeBookEdit message)
		{
		}

		public virtual void HandleMcpeNpcRequest(McpeNpcRequest message)
		{
		}

		public virtual void HandleMcpeModalFormRequest(McpeModalFormRequest message)
		{
		}

		public virtual void HandleMcpeServerSettingsResponse(McpeServerSettingsResponse message)
		{
		}

		public virtual void HandleMcpeShowProfile(McpeShowProfile message)
		{
		}

		public virtual void HandleMcpeSetDefaultGameType(McpeSetDefaultGameType message)
		{
		}

		public virtual void HandleMcpeRemoveObjective(McpeRemoveObjective message)
		{
		}

		public virtual void HandleMcpeSetDisplayObjective(McpeSetDisplayObjective message)
		{
		}

		public virtual void HandleMcpeSetScore(McpeSetScore message)
		{
		}

		public virtual void HandleMcpeLabTable(McpeLabTable message)
		{
		}

		public virtual void HandleMcpeUpdateBlockSynced(McpeUpdateBlockSynced message)
		{
		}

		public virtual void HandleMcpeMoveEntityDelta(McpeMoveEntityDelta message)
		{
		}

		public virtual void HandleMcpeSetScoreboardIdentity(McpeSetScoreboardIdentity message)
		{
		}

		public virtual void HandleMcpeUpdateSoftEnum(McpeUpdateSoftEnum message)
		{
		}

		public virtual void HandleMcpeNetworkStackLatency(McpeNetworkStackLatency message)
		{
			var packet = McpeNetworkStackLatency.CreateObject();
			packet.timestamp = message.timestamp;
			packet.unknownFlag = 0;

			Client.SendPacket(packet);
		}

		public virtual void HandleMcpeScriptCustomEvent(McpeScriptCustomEvent message)
		{
		}

		public virtual void HandleMcpeSpawnParticleEffect(McpeSpawnParticleEffect message)
		{
		}

		public virtual void HandleMcpeAvailableEntityIdentifiers(McpeAvailableEntityIdentifiers message)
		{
		}

		public virtual void HandleMcpeLevelSoundEventV2(McpeLevelSoundEventV2 message)
		{
		}

		public virtual void HandleMcpeNetworkChunkPublisherUpdate(McpeNetworkChunkPublisherUpdate message)
		{
		}

		public virtual void HandleMcpeBiomeDefinitionList(McpeBiomeDefinitionList message)
		{
		}

		public virtual void HandleMcpeLevelSoundEvent(McpeLevelSoundEvent message)
		{
		}

		public virtual void HandleMcpeLevelEventGeneric(McpeLevelEventGeneric message)
		{
		}

		public virtual void HandleMcpeLecternUpdate(McpeLecternUpdate message)
		{
		}

		public virtual void HandleMcpeVideoStreamConnect(McpeVideoStreamConnect message)
		{
		}

		public virtual void HandleMcpeClientCacheStatus(McpeClientCacheStatus message)
		{
		}

		public virtual void HandleMcpeOnScreenTextureAnimation(McpeOnScreenTextureAnimation message)
		{
			Log.Warn($"Got onscreen animation {message.effectId}");
		}

		public virtual void HandleMcpeMapCreateLockedCopy(McpeMapCreateLockedCopy message)
		{
		}

		public virtual void HandleMcpeStructureTemplateDataExportRequest(McpeStructureTemplateDataExportRequest message)
		{
		}

		public virtual void HandleMcpeStructureTemplateDataExportResponse(McpeStructureTemplateDataExportResponse message)
		{
		}

		public virtual void HandleMcpeUpdateBlockProperties(McpeUpdateBlockProperties message)
		{
		}

		public virtual void HandleMcpeClientCacheBlobStatus(McpeClientCacheBlobStatus message)
		{
		}

		public virtual void HandleMcpeClientCacheMissResponse(McpeClientCacheMissResponse message)
		{
		}

		public virtual void HandleMcpeNetworkSettings(McpeNetworkSettings message)
		{
		}

		public virtual void HandleMcpeCreativeContent(McpeCreativeContent message)
		{
		}

		public void HandleMcpePlayerEnchantOptions(McpePlayerEnchantOptions message)
		{
		}

		public virtual void HandleMcpeItemStackResponse(McpeItemStackResponse message)
		{
		}

		/// <inheritdoc />
		public virtual void HandleMcpeItemComponent(McpeItemComponent message)
		{
			
		}

		/// <inheritdoc />
		public virtual void HandleMcpeUpdateSubChunkBlocksPacket(McpeUpdateSubChunkBlocksPacket message)
		{
			
		}

		/// <inheritdoc />
		public void HandleMcpeSubChunkPacket(McpeSubChunkPacket message)
		{
			
		}

		/// <inheritdoc />
		public void HandleMcpeDimensionData(McpeDimensionData message)
		{
			
		}

		/// <inheritdoc />
		public void HandleMcpeUpdateAbilities(McpeUpdateAbilities message)
		{
			Client.permissionLevel = (PermissionLevel) message.playerPermissions;
		}

		/// <inheritdoc />
		public void HandleMcpeUpdateAdventureSettings(McpeUpdateAdventureSettings message)
		{
			
		}

		public virtual void HandleMcpeAlexEntityAnimation(McpeAlexEntityAnimation message)
		{
		}

		public virtual void HandleFtlCreatePlayer(FtlCreatePlayer message)
		{
		}

		public void HandleMcpeTrimData(McpeTrimData message)
		{
			
		}

		public void HandleMcpeOpenSign(McpeOpenSign message)
		{
			
		}

		public void HandleMcpeEmote(McpeEmotePacket message)
		{

		}

		public void HandleMcpeEmoteList(McpeEmoteList message)
		{

		}

		public void HandleMcpePermissionRequest(McpePermissionRequest message)
		{

		}

		public void HandleMcpePlayerFog(McpePlayerFog message)
		{

		}

		public virtual void HandleMcpeAnimateEntity(McpeAnimateEntity message)
		{
			Log.Warn($"Got entity animation {message.animationName}");
		}

		public void HandleMcpeServerboundLoadingScreen(McpeServerboundLoadingScreen message)
		{

		}

		public void HandleMcpeCloseForm(McpeCloseForm message)
		{

		}
	}

	public class DefaultMessageHandler : McpeClientMessageHandlerBase
	{
		public DefaultMessageHandler(MiNetClient client) : base(client)
		{
		}
	}
}