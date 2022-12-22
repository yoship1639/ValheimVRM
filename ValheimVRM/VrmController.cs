using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CloudinaryDotNet;
using UnityEngine;
using VRM;
using Random = UnityEngine.Random;

namespace ValheimVRM
{
    public class VrmController : MonoBehaviour
    {
        const int MaxPacketSize = 1024 * 500;
        
        class LoadingProcess
        {
	        public bool UseExistingData = false;
	        public List<byte[]> Packets = new List<byte[]>();
	        public bool PacketsDone = false;
			
	        public bool UseExistingSettings = false;
	        public string Settings = null;
	        public bool SettingsDone = false;

	        public bool IsLoaded() => (UseExistingData ? true : PacketsDone) && (UseExistingSettings ? true : SettingsDone);

	        public byte[] GetVrmData()
	        {
		        int size = 0;
		        foreach (var packet in Packets)
		        {
			        size += packet.Length;
		        }

		        byte[] bytes = new byte[size];

		        int s = 0;
		        foreach (var packet in Packets)
		        {
			        Array.Copy(packet, 0, bytes, s, packet.Length);
			        s += packet.Length;
		        }

		        return bytes;
	        }
        }

        class SpringBoneState
        {
	        public VRMSpringBone SpringBone;
	        public Vector3 InitialGravityForce;
        }

        class WindItem
        {
	        public float Time;
	        public Vector3 Dir;
	        public float Rise;
	        public float Sit;
	        public float MaxFactor;

	        public Vector3 CachedWindForce;
        }
        
        private static Dictionary<string, LoadingProcess> _activeLoadings = new Dictionary<string, LoadingProcess>();
        private static Material _playerSizeGizmoMaterial;
        
        private ZNetView view;
        private Player player;
        private string playerName;

        public GameObject visual;
        
        private CapsuleCollider playerCollider;
        private GameObject playerSizeGizmo;

        private SpringBoneState[] springBones = new SpringBoneState[0];
        private Vector2 windIntervalRange = new Vector2(0.7f, 1.9f);
        private float windDirRange = 0.2f;
        private Vector2 windRiseRange = new Vector2(0.4f, 0.6f);
        private Vector2 windSitRange = new Vector2(1.3f, 1.8f);
        private Vector2 windStrengthRange = new Vector2(0.03f, 0.12f);
        private WindItem[] windItems;
        private float windCoverPercentage;
        private float windCoverUpdateTimer;

        private Vector3[] windCoverRays = new Vector3[]
        {
			new Vector3(0, 0, 1),
			(Quaternion.Euler(0, 0, 45 * 0) * new Vector3(0.5f, 0, 1)).normalized,
			(Quaternion.Euler(0, 0, 45 * 1) * new Vector3(0.5f, 0, 1)).normalized,
			(Quaternion.Euler(0, 0, 45 * 2) * new Vector3(0.5f, 0, 1)).normalized,
			(Quaternion.Euler(0, 0, 45 * 3) * new Vector3(0.5f, 0, 1)).normalized,
			(Quaternion.Euler(0, 0, 45 * 4) * new Vector3(0.5f, 0, 1)).normalized,
			(Quaternion.Euler(0, 0, 45 * 5) * new Vector3(0.5f, 0, 1)).normalized,
			(Quaternion.Euler(0, 0, 45 * 6) * new Vector3(0.5f, 0, 1)).normalized,
			(Quaternion.Euler(0, 0, 45 * 7) * new Vector3(0.5f, 0, 1)).normalized,
        };

        public static VrmController GetLocalController()
        {
	        foreach (var controller in FindObjectsOfType<VrmController>())
	        {
		        if (controller.view.GetZDO() != null && controller.view.IsOwner() || controller.view.GetZDO() == null)
		        {
			        return controller;
		        }
	        }

	        return null;
        }
        
        private string GetPeerName(long peerId) => ZNet.instance?.GetPeer(peerId)?.m_playerName ?? peerId.ToString();

        public void ReloadSpringBones()
        {
	        foreach (var bone in springBones)
	        {
		        if (bone.SpringBone != null)
		        {
			        bone.SpringBone.m_gravityDir = bone.InitialGravityForce.normalized;
			        bone.SpringBone.m_gravityPower = bone.InitialGravityForce.magnitude;
		        }
	        }

	        springBones = GetComponentsInChildren<VRMSpringBone>().Select(bone => new SpringBoneState{ SpringBone = bone, InitialGravityForce = bone.m_gravityDir * bone.m_gravityPower}).ToArray();
	        windItems = new WindItem[Settings.globalSettings.AllowIndividualWinds ? springBones.Length : 1];
	        for (int i = 0; i < windItems.Length; i++)
	        {
		        windItems[i] = new WindItem();
	        }
	        windCoverUpdateTimer = 0;
        }

        public void ResetSpringBonesWind()
        {
	        foreach (var bone in springBones)
	        {
		        if (bone.SpringBone != null)
		        {
			        bone.SpringBone.m_gravityDir = bone.InitialGravityForce.normalized;
			        bone.SpringBone.m_gravityPower = bone.InitialGravityForce.magnitude;
		        }
	        }
        }

        private void Awake()
        {
	        view = GetComponent<ZNetView>();
	        player = GetComponent<Player>();
	        
	        if (_playerSizeGizmoMaterial == null)
	        {
		        _playerSizeGizmoMaterial = new Material(Shader.Find("Standard"));
		        _playerSizeGizmoMaterial.SetFloat("_Mode", 2);
		        _playerSizeGizmoMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
		        _playerSizeGizmoMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
		        _playerSizeGizmoMaterial.SetInt("_ZWrite", 0);
		        _playerSizeGizmoMaterial.DisableKeyword("_ALPHATEST_ON");
		        _playerSizeGizmoMaterial.EnableKeyword("_ALPHABLEND_ON");
		        _playerSizeGizmoMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
		        _playerSizeGizmoMaterial.SetFloat("_GlossMapScale", 0);
		        _playerSizeGizmoMaterial.renderQueue = 3000;
		        _playerSizeGizmoMaterial.color = new Color(1.0f, 0.0f, 0.0f, 0.3f);
	        }
            
	        playerCollider = GetComponent<CapsuleCollider>();

	        if (Settings.globalSettings.DrawPlayerSizeGizmo) ActivateSizeGizmo();

	        if (Game.instance != null)
	        {
		        playerName = GetComponent<Player>().GetPlayerName();
		        if (playerName == "" || playerName == "...") playerName = Game.instance.GetPlayerProfile().GetName();
	        }
	        else
	        {
		        var index = FejdStartup.instance.GetField<FejdStartup, int>("m_profileIndex");
		        var profiles = FejdStartup.instance.GetField<FejdStartup, List<PlayerProfile>>("m_profiles");
		        if (index >= 0 && index < profiles.Count) playerName = profiles[index].GetName();
	        }

	        if (view.GetZDO() == null) return;
	        
	        view.Register(nameof(RPC_QueryAll), new Action<long>(RPC_QueryAll));
	        view.Register(nameof(RPC_SendHashes), new Action<long, string, ZPackage, ZPackage>(RPC_SendHashes));
	        view.Register(nameof(RPC_QueryData), new Action<long, string>(RPC_QueryData));
	        view.Register(nameof(RPC_SendDataPacket), new Action<long, string, ZPackage>(RPC_SendDataPacket));
	        view.Register(nameof(RPC_DataPacketCallback), new Action<long, string, int>(RPC_DataPacketCallback));
	        view.Register(nameof(RPC_QuerySettings), new Action<long, string>(RPC_QuerySettings));
	        view.Register(nameof(RPC_SendSettings), new Action<long, string, string>(RPC_SendSettings));
        }
        
        private void Update()
        {
	        if (playerCollider == null || playerSizeGizmo == null) return;

	        playerSizeGizmo.transform.position = playerCollider.bounds.center;
	        playerSizeGizmo.transform.localScale = new Vector3(playerCollider.bounds.size.x, playerCollider.bounds.size.y / 2, playerCollider.bounds.size.z);
        }

        private void FixedUpdate()
        {
	        if (springBones.Length > 0 && !Settings.globalSettings.ForceWindDisabled)
	        {
		        Vector3 worldWindDir = EnvMan.instance?.GetWindDir() ?? Vector3.back;
		        float worldWindIntensity = EnvMan.instance?.GetWindIntensity() ?? 0.3f;
		        
		        windCoverUpdateTimer -= Time.deltaTime;
		        if (windCoverUpdateTimer < 0)
		        {
			        windCoverUpdateTimer = 1;
			        
			        var mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

			        windCoverPercentage = 0;
			        foreach (var ray in windCoverRays)
			        {
				        var actualRay = Quaternion.LookRotation(-worldWindDir) * ray;
				        if (Physics.Raycast(player.GetCenterPoint() + actualRay * 0.5f, actualRay, out RaycastHit _, 30f - 0.5f, mask))
					        ++windCoverPercentage;
			        }

			        windCoverPercentage /= windCoverRays.Length;
		        }
		        
		        foreach (var windItem in windItems)
		        {
			        windItem.Time -= Time.deltaTime;
			        if (windItem.Time < 0)
			        {
				        windItem.Time = Random.Range(windIntervalRange.x, windIntervalRange.y);
				        windItem.Dir = (worldWindDir +
						        new Vector3(
							        Random.Range(-windDirRange, windDirRange),
							        Random.Range(-windDirRange, windDirRange),
							        Random.Range(-windDirRange, windDirRange)
						        )
					        ).normalized;
				        windItem.Rise = Random.Range(windRiseRange.x, windRiseRange.y);
				        windItem.Sit = Random.Range(windSitRange.x, windSitRange.y);
				        windItem.MaxFactor = Random.Range(windStrengthRange.x, windStrengthRange.y);
			        }
			        
			        float factor = windItem.Time < windItem.Rise
				        ? windItem.MaxFactor * windItem.Time / windItem.Rise
				        : windItem.MaxFactor * (1 - (windItem.Time - windItem.Rise) / windItem.Sit);

			        windItem.CachedWindForce = windItem.Dir * (factor * (worldWindIntensity * Mathf.Lerp(1, 0.1f, windCoverPercentage) * 3));
		        }

		        for (int i = 0; i < springBones.Length; i++)
		        {
			        Vector3 sumForce = springBones[i].InitialGravityForce + (Settings.globalSettings.AllowIndividualWinds ? windItems[i] : windItems[0]).CachedWindForce;
			        springBones[i].SpringBone.m_gravityDir = sumForce.normalized;
			        springBones[i].SpringBone.m_gravityPower = sumForce.magnitude;
		        }
	        }
        }

        public void ActivateSizeGizmo()
        {
	        if (playerSizeGizmo != null) return;
            
	        playerSizeGizmo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
	        playerSizeGizmo.GetComponent<MeshRenderer>().material = _playerSizeGizmoMaterial;
	        Destroy(playerSizeGizmo.GetComponent<Collider>());
        }

        public void DeactivateSizeGizmo()
        {
	        if (playerSizeGizmo == null) return;
            
	        Destroy(playerSizeGizmo);
        }

        public static void CleanupLoadings()
        {
            _activeLoadings.Clear();
        }

        private void SendDataPacket(long target, string vrmName, int packetIndex)
        {
	        VRM vrm = VrmManager.VrmDic[vrmName];
            
            int packetCount = Mathf.CeilToInt((float)vrm.Src.Length / MaxPacketSize);

            ZPackage package = new ZPackage();

            if (packetIndex >= packetCount)
            {
	            package.Write(false);
                view.InvokeRPC(target, nameof(RPC_SendDataPacket), vrmName, package);
                return;
            }
					
            int packetSize = packetIndex < packetCount - 1
                ? MaxPacketSize
                : vrm.Src.Length % packetCount;
            byte[] packetData = new byte[packetSize];
            Array.Copy(vrm.Src, packetIndex * MaxPacketSize, packetData, 0, packetSize);

            package.Write(true);
            package.Write(packetIndex);
            package.Write(packetCount);
            package.Write(packetData);
            view.InvokeRPC(target, nameof(RPC_SendDataPacket), vrmName, package);
							
            Debug.Log($"[ValheimVRM] sent {vrmName} packet {packetIndex + 1} of {packetCount} to {GetPeerName(target)}");
        }
				
        public void ShareVrm(bool delay = true)
        {
	        if (view.GetZDO() == null) return;
	        if (!view.IsOwner()) return;

	        if (delay) StartCoroutine(nameof(ShareVrmDelayed));
	        else DoShareVrm();
        }

        public IEnumerator ShareVrmDelayed()
        {
	        yield return new WaitForSeconds(Settings.globalSettings.StartVrmShareDelay);
	        
	        DoShareVrm();
        }

        private void DoShareVrm()
        {
	        var settings = Settings.GetSettings(playerName);

	        if (settings != null && settings.AllowShare && VrmManager.VrmDic.ContainsKey(playerName))
	        {
		        var vrm = VrmManager.VrmDic[playerName];
	            
		        Debug.Log($"[ValheimVRM] sharing {playerName} vrm, hashes are {vrm.SrcHash.GetHaxadecimalString()} {vrm.SettingsHash.GetHaxadecimalString()}");

		        if (ZNet.instance.IsServer())
		        {
			        var peers = ZNet.instance.GetPeers();

			        foreach (var peer in peers)
			        {
				        view.InvokeRPC(peer.m_uid, nameof(RPC_SendHashes), playerName, new ZPackage(vrm.SrcHash), new ZPackage(vrm.SettingsHash));
			        }
		        }
		        else
		        {
			        view.InvokeRPC(ZNet.instance.GetServerPeer().m_uid, nameof(RPC_SendHashes), playerName, new ZPackage(vrm.SrcHash), new ZPackage(vrm.SettingsHash));
		        }
	        }
        }
        
        public void QueryAllVrm(bool delayed = true)
        {
	        if (ZNet.instance == null) return;
	        if (ZNet.instance.IsServer()) return;
	        if (view.GetZDO() == null) return;
	        if (!view.IsOwner()) return;

	        if (delayed) StartCoroutine(nameof(QueryAllVrmDelayed));
	        else DoQueryAllVrm();
        }

        public IEnumerator QueryAllVrmDelayed()
        {
	        yield return new WaitForSeconds(Settings.globalSettings.StartVrmShareDelay);
	        
	        DoQueryAllVrm();
        }

        private void DoQueryAllVrm()
        {
	        view.InvokeRPC(ZNet.instance.GetServerPeer().m_uid, nameof(RPC_QueryAll));
        }

        public void RPC_QueryAll(long sender)
        {
	        var peer = ZNet.instance.GetPeer(sender);
	        foreach (var kvp in VrmManager.VrmDic)
	        {
		        if (kvp.Key == peer.m_playerName) continue;

		        var settings = Settings.GetSettings(kvp.Key);
		        if (!settings.AllowShare) continue;

		        view.InvokeRPC(sender, nameof(RPC_SendHashes), kvp.Key, new ZPackage(kvp.Value.SrcHash), new ZPackage(kvp.Value.SettingsHash));
	        }
        }
        
        public void RPC_SendHashes(long sender, string vrmName, ZPackage dataHash, ZPackage settingsHash)
        {
	        Debug.Log($"[ValheimVRM] got {vrmName} vrm hashes from {GetPeerName(sender)}: {dataHash.GetArray().GetHaxadecimalString()}, {settingsHash.GetArray().GetHaxadecimalString()}");

	        if (!Settings.globalSettings.AcceptVrmSharing)
	        {
		        if (ZNet.instance != null && ZNet.instance.IsServer())
		        {
			        Debug.Log($"[ValheimVRM] accepting shared vrm is disabled, but that is ignored since we are server");
		        }
		        else
		        {
			        return;
		        }
	        }
	        
			if (VrmManager.VrmDic.ContainsKey(vrmName))
			{
				var vrm = VrmManager.VrmDic[vrmName];

				bool dataEqual = Utils.CompareArrays(vrm.SrcHash, dataHash.GetArray());
				bool settingsEqual = Utils.CompareArrays(vrm.SettingsHash, settingsHash.GetArray());
				
				bool required = !dataEqual || !settingsEqual;
				
				Debug.Log($"[ValheimVRM] current {vrmName} vrm hashes are: {vrm.SrcHash.GetHaxadecimalString()}, {vrm.SettingsHash.GetHaxadecimalString()}");
				if (required)
				{
					List<string> req = new List<string>();
					if (!dataEqual) req.Add("new data");
					if (!settingsEqual) req.Add("new settings");
					Debug.Log($"[ValheimVRM] requesting {String.Join(", ", req)}");
					
					var process = new LoadingProcess();
					
					if (!dataEqual)
					{
						view.InvokeRPC(sender, nameof(RPC_QueryData), vrmName);
						required = true;
					}
					else
					{
						process.UseExistingData = true;
					}

					if (!settingsEqual)
					{
						view.InvokeRPC(sender , nameof(RPC_QuerySettings), vrmName);
						required = true;
					}
					else
					{
						process.UseExistingSettings = true;
					}

					_activeLoadings[vrmName] = process;
				}
				else
				{
					Debug.Log($"[ValheimVRM] everything is up to date");
				}
			}
			else
			{
				Debug.Log($"[ValheimVRM] current {vrmName} vrm hashes are: none, none");
				Debug.Log($"[ValheimVRM] requesting new data, new settings");
				
				view.InvokeRPC(sender, nameof(RPC_QueryData), vrmName);
				view.InvokeRPC(sender, nameof(RPC_QuerySettings), vrmName);
			}
		}

		public void RPC_QueryData(long sender, string vrmName)
		{
			SendDataPacket(sender, vrmName, 0);
		}

		public void RPC_SendDataPacket(long sender, string vrmName, ZPackage package)
		{
			var process = _activeLoadings.GetOrCreateDefault(vrmName);

			bool state = package.ReadBool();
			
			if (state == false)
			{
				process.PacketsDone = true;
				Debug.Log($"[ValheimVRM] received all {vrmName} vrm data packets from {GetPeerName(sender)}");
			}
			else
			{
				int index = package.ReadInt();
				int total = package.ReadInt();
				
				process.Packets.Add(package.ReadByteArray());
				Debug.Log($"[ValheimVRM] received {vrmName} vrm data packet {index + 1} of {total} from {GetPeerName(sender)}");
				
				view.InvokeRPC(sender, nameof(RPC_DataPacketCallback), vrmName, index);
			}

			if (process.IsLoaded()) OnLoadingFinished(vrmName, process);
		}

		public void RPC_DataPacketCallback(long sender, string vrmName, int packetIndex)
		{
			SendDataPacket(sender, vrmName, packetIndex + 1);
		}

		public void RPC_QuerySettings(long sender, string vrmName)
		{
			view.InvokeRPC(sender, nameof(RPC_SendSettings), vrmName, Settings.GetSettings(vrmName).ToStringDiffOnly());
			
			Debug.Log($"[ValheimVRM] sent {vrmName} vrm settings to {GetPeerName(sender)}");
		}

		public void RPC_SendSettings (long sender, string vrmName, string settingsString)
		{
			var process = _activeLoadings.GetOrCreateDefault(vrmName);
			
			process.Settings = settingsString;
			process.SettingsDone = true;
			
			Debug.Log($"[ValheimVRM] received {vrmName} vrm settings from {GetPeerName(sender)}");
			
			if (process.IsLoaded()) OnLoadingFinished(vrmName, process);
		}

		private void OnLoadingFinished(string vrmName, LoadingProcess process)
		{
			foreach (var player in Player.GetAllPlayers())
			{
				if (player.GetPlayerName() == vrmName)
				{
					var sharedPath = Path.Combine(Environment.CurrentDirectory, "ValheimVRM", "Shared");
					var vrmPath = Path.Combine(sharedPath, $"{vrmName}.vrm");
					var settingsPath = Path.Combine(sharedPath, $"settings_{vrmName}.txt");

					if (!Directory.Exists(sharedPath))
					{
						Directory.CreateDirectory(sharedPath);
					}

					if (!process.UseExistingSettings)
					{
						Settings.AddSettingsRaw(vrmName, process.Settings.Split('\n'));
						File.WriteAllText(settingsPath, process.Settings);
					}
					
					if (!process.UseExistingData)
					{
						var settings = Settings.GetSettings(vrmName);
						
						var scale = settings.ModelScale;

						byte[] vrmBytes = process.GetVrmData();
						VRM newVrm = new VRM(VRM.ImportVisual(process.GetVrmData(), vrmPath, scale), vrmName);
						newVrm = VrmManager.RegisterVrm(newVrm, player.GetComponentInChildren<LODGroup>());
						if (newVrm != null)
						{
							newVrm.Source = VRM.SourceType.Shared;

							newVrm.Src = vrmBytes;
							newVrm.RecalculateSrcBytesHash();
							if (!ZNet.instance.IsServer()) newVrm.Src = null;
							
							File.WriteAllBytes(vrmPath, vrmBytes);
							
							if (!process.UseExistingSettings)
							{
								newVrm.RecalculateSettingsHash();
							}
							
							newVrm.SetToPlayer(player);
						}
					}

					VRM vrm = VrmManager.VrmDic[vrmName];

					if (ZNet.instance.IsServer())
					{
						var peers = ZNet.instance.GetPeers();
						peers.Remove(ZNet.instance.GetPeerByPlayerName(vrmName));

						foreach (var peer in peers)
						{
							view.InvokeRPC(peer.m_uid, nameof(RPC_SendHashes), vrmName, new ZPackage(vrm.SrcHash), new ZPackage(vrm.SettingsHash));
						}
					}

					break;
				}
			}
			
			_activeLoadings.Remove(vrmName);
		}
    }
}