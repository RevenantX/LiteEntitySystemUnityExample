using System;
using System.Net;
using System.Net.Sockets;
using LiteEntitySystem;
using Code.Shared;
using LiteEntitySystem.Transport;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Code.Client
{
    public class ClientLogic : MonoBehaviour, INetEventListener
    {
        [SerializeField] private ClientPlayerView _clientPlayerViewPrefab;
        [SerializeField] private RemotePlayerView _remotePlayerViewPrefab;
        [SerializeField] private Text _debugText;
        [SerializeField] private ShootEffect _shootEffectPrefab;
        [SerializeField] private HitEffect _hitEffect;

        private Action<DisconnectInfo> _onDisconnected;
        private GamePool<ShootEffect> _shootsPool;
        private GamePool<HitEffect> _hitsPool;

        private NetManager _netManager;
        private NetDataWriter _writer;
        private NetPacketProcessor _packetProcessor;
        
        private string _userName;
        private NetPeer _server;
        private ClientEntityManager _entityManager;
        private int _ping;

        private int PacketsInPerSecond;
        private int BytesInPerSecond;
        private int PacketsOutPerSecond;
        private int BytesOutPerSecond;

        private float _secondTimer;
        private BasePlayer _ourPlayer;
        
        public static ClientLogic Instance { get; private set; }

        static ClientLogic()
        {
            LiteEntitySystem.Logger.LoggerImpl = new UnityLogger();
        }
        
        private ShootEffect ShootEffectContructor()
        {
            var eff = Instantiate(_shootEffectPrefab);
            eff.Init(e =>
            {
                if(_shootsPool.Put(e) == false)
                    Destroy(e.gameObject);
            });
            return eff;
        }
        
        private HitEffect HitEffectContructor()
        {
            var eff = Instantiate(_hitEffect);
            eff.Init(e =>
            {
                if(_hitsPool.Put(e) == false)
                    Destroy(e.gameObject);
            });
            return eff;
        }
        
        private void Awake()
        {
            EntityManager.RegisterFieldType<Vector2>(Vector2.Lerp);
            
            Instance = this;
            _userName = Environment.MachineName + " " + Random.Range(0, 100000);
            _writer = new NetDataWriter();

            _shootsPool = new GamePool<ShootEffect>(ShootEffectContructor, 200);
            _hitsPool = new GamePool<HitEffect>(HitEffectContructor, 200);
            _packetProcessor = new NetPacketProcessor();
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                IPv6Enabled = false,
                SimulateLatency = true,
                SimulationMinLatency = 50,
                SimulationMaxLatency = 60,
                SimulatePacketLoss = false,
                SimulationPacketLossChance = 10
            };
            _netManager.Start();
        }

        private void Update()
        {
            _netManager.PollEvents();
            _secondTimer += Time.deltaTime;
            if(_secondTimer >= 1f)
            {
                _secondTimer -= 1f;
                var stats = _netManager.Statistics;
                BytesInPerSecond = (int)stats.BytesReceived;
                PacketsInPerSecond = (int)stats.PacketsReceived;
                BytesOutPerSecond = (int)stats.BytesSent;
                PacketsOutPerSecond = (int)stats.PacketsSent;
                stats.Reset();
            }
            if (_entityManager != null)
            {
                _entityManager.Update();
                _debugText.text = $@"
C_ServerTick: {_entityManager.ServerTick}
C_Tick: {_entityManager.Tick}
C_LPRCS: {_entityManager.LastProcessedTick}
C_StoredCommands: {_entityManager.StoredCommands}
C_Entities: {_entityManager.EntitiesCount}
C_ServerInputBuffer: {_entityManager.ServerInputBuffer}
C_LerpBufferCount: {_entityManager.LerpBufferCount}
C_LerpBufferTime: {_entityManager.LerpBufferTimeLength}
Jitter: {_entityManager.NetworkJitter}
Ping: {_ping}
IN: {BytesInPerSecond/1000f} KB/s({PacketsInPerSecond})
OUT: {BytesOutPerSecond/1000f} KB/s({PacketsOutPerSecond})";
            }
            else
            {
                _debugText.text = "Disconnected";
            }
        }

        private void OnDestroy()
        {
            _netManager.Stop();
        }

        public void SpawnShoot(Vector2 from, Vector2 to)
        {
            _shootsPool.Get().Spawn(from, to);
        }

        public void SpawnHit(Vector2 from)
        {
            _hitsPool.Get().Spawn(from);
        }

        private void SendPacket<T>(T packet, DeliveryMethod deliveryMethod) where T : class, new()
        {
            if (_server == null)
                return;
            _writer.Reset();
            _writer.Put((byte) PacketType.Serialized);
            _packetProcessor.Write(_writer, packet);
            _server.Send(_writer, deliveryMethod);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            Debug.Log("[C] Connected to server: " + peer);
            _server = peer;
            
            SendPacket(new JoinPacket {UserName = _userName}, DeliveryMethod.ReliableOrdered);

            var typesMap = new EntityTypesMap<GameEntities>()
                .Register(GameEntities.Player, e => new BasePlayer(e))
                .Register(GameEntities.PlayerController, e => new BasePlayerController(e))
                .Register(GameEntities.GameWeapon, e => new GameWeapon(e))
                .Register(GameEntities.WeaponItem, e => new WeaponItem(e))
                .Register(GameEntities.Physics, e => new UnityPhysicsManager(e).Init(transform))
                .Register(GameEntities.Projectile, e => new SimpleProjectile(e));

            _entityManager = ClientEntityManager.Create<PlayerInputPacket>(
                typesMap, 
                new LiteNetLibNetPeer(peer, true), 
                (byte)PacketType.EntitySystem, 
                NetworkGeneral.GameFPS);
            _entityManager.GetEntities<BasePlayer>().SubscribeToConstructed(player =>
            {
                if (player.IsLocalControlled)
                {
                    _ourPlayer = player;
                    ClientPlayerView.Create(_clientPlayerViewPrefab, (BasePlayerController)_ourPlayer.Controller);
                }
                else
                {
                    //Debug.Log($"[C] Player joined: {player.Name}");
                    RemotePlayerView.Create(_remotePlayerViewPrefab, player);
                }
            }, true);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _server = null;
            _entityManager = null;
            Debug.Log("[C] Disconnected from server: " + disconnectInfo.Reason);
            if (_onDisconnected != null)
            {
                _onDisconnected(disconnectInfo);
                _onDisconnected = null;
            }
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.Log("[C] NetworkError: " + socketError);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            byte packetType = reader.PeekByte();
            var pt = (PacketType) packetType;
            switch (pt)
            {
                case PacketType.EntitySystem:
                    _entityManager.Deserialize(reader.AsReadOnlySpan());
                    break;
                
                case PacketType.Serialized:
                    reader.GetByte();
                    _packetProcessor.ReadAllPackets(reader);
                    break;
     
                default:
                    Debug.Log("Unhandled packet: " + pt);
                    break;
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
            UnconnectedMessageType messageType)
        {

        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            _ping = latency;
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            request.Reject();
        }

        public void Connect(string ip, Action<DisconnectInfo> onDisconnected)
        {
            _onDisconnected = onDisconnected;
            _netManager.Connect(ip, 10515, "ExampleGame");
        }
    }
}