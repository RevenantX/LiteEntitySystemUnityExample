using System;
using System.Net;
using System.Net.Sockets;
using LiteEntitySystem;
using Code.Shared;
using LiteEntitySystem.Transport;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Code.Server
{
    public class ServerLogic : MonoBehaviour, INetEventListener
    {
        private NetManager _netManager;
        private NetPacketProcessor _packetProcessor;
        public ushort Tick => _serverEntityManager.Tick;
        private ServerEntityManager _serverEntityManager;

        static ServerLogic()
        {
            LiteEntitySystem.Logger.LoggerImpl = new UnityLogger();
        }

        private void Awake()
        {
            EntityManager.RegisterFieldType<Vector2>(Vector2.Lerp);
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                PacketPoolSize = 1000,
                SimulateLatency = true,
                SimulationMinLatency = 50,
                SimulationMaxLatency = 60,
                SimulatePacketLoss = false,
                SimulationPacketLossChance = 10
            };
#if UNITY_SERVER
            Application.targetFrameRate = NetworkGeneral.GameFPS;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
#endif

            _packetProcessor = new NetPacketProcessor();
            _packetProcessor.SubscribeReusable<JoinPacket, NetPeer>(OnJoinReceived);

            var typesMap = new EntityTypesMap<GameEntities>()
                .Register(GameEntities.Player, e => new BasePlayer(e))
                .Register(GameEntities.PlayerController, e => new BasePlayerController(e))
                .Register(GameEntities.BotController, e => new ServerBotController(e))
                .Register(GameEntities.GameWeapon, e => new GameWeapon(e))
                .Register(GameEntities.WeaponItem, e => new WeaponItem(e))
                .Register(GameEntities.Physics, e => new UnityPhysicsManager(e).Init(transform))
                .Register(GameEntities.Projectile, e => new SimpleProjectile(e));
            
            _serverEntityManager = ServerEntityManager.Create<PlayerInputPacket>(
                typesMap,
                (byte)PacketType.EntitySystem, 
                NetworkGeneral.GameFPS, 
                ServerSendRate.EqualToFPS);

            _serverEntityManager.AddSignleton<UnityPhysicsManager>();

            for (int i = 0; i < 500; i++)
            {
                int botNum = i;
                var botPlayer = _serverEntityManager.AddEntity<BasePlayer>(e =>
                {
                    e.Name.Value = $"Bot_{botNum}";
                    e.Spawn(new Vector2(Random.Range(-80f, 80f), Random.Range(-80f, 80f)));
                });
                _serverEntityManager.AddAIController<ServerBotController>(e => e.StartControl(botPlayer));
            }
            
            _netManager.Start(10515);
        }

        private void OnDestroy()
        {
            _netManager.Stop();
            _serverEntityManager = null;
        }

        private void Update()
        {
            _netManager.PollEvents();
            _serverEntityManager?.Update();
        }

        private void OnJoinReceived(JoinPacket joinPacket, NetPeer peer)
        {
            Debug.Log("[S] Join packet received: " + joinPacket.UserName);
            
            var serverPlayer = _serverEntityManager.AddPlayer(new LiteNetLibNetPeer(peer, true));
            var player = _serverEntityManager.AddEntity<BasePlayer>(e =>
            {
                e.Spawn(new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f)));
                e.Name.Value = joinPacket.UserName;
            });
            _serverEntityManager.AddController<BasePlayerController>(serverPlayer, player);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            Debug.Log("[S] Player connected: " + peer);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log("[S] Player disconnected: " + disconnectInfo.Reason);

            if (peer.Tag != null)
            {
                _serverEntityManager.RemovePlayer((LiteNetLibNetPeer)peer.Tag);
            }
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.Log("[S] NetworkError: " + socketError);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            byte packetType = reader.PeekByte();
            switch ((PacketType)packetType)
            {
                case PacketType.EntitySystem:
                    _serverEntityManager.Deserialize((LiteNetLibNetPeer)peer.Tag, reader.AsReadOnlySpan());
                    break;
                
                case PacketType.Serialized:
                    reader.GetByte();
                    _packetProcessor.ReadAllPackets(reader, peer);
                    break;
                
                default:
                    Debug.Log("Unhandled packet: " + packetType);
                    break;
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader,
            UnconnectedMessageType messageType)
        {

        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            request.AcceptIfKey("ExampleGame");
        }
    }
}