using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using System.IO;
using System;

/* To Do:

- Set up server simulation and game-state-send-out system 
    * not sure but I feel like server will have to roll back, how else could it send timely game state updates?

*/

/* Notes:

- Eventually add feature to not send some data of players more than a variable distance away

*/

/* Message Structure:

Message byte headers

0 - blank/error

1 - Server sending a networkID for handshake
    - header (byte)
    - playerID (byte)
    - current tick (uint)
    - connected bools (MAX_PLAYERS * byte) (1 byte for each player to show if they're connected or not, 0 = false (disconnected), 1 = true (connected))

2 - New client connected / spawn player object
    - header (byte)
    - playerID (byte)

3 - Client disconnected / destroy player object
    - header (byte)
    - playerID (byte)

4 - inputs from client
    - header (byte)
    - playerID (byte)
    - tick (uint)
    - WASD (4 bytes)

5 - inputs from server
    - header (byte)
    - playerID (byte)
    - tick (uint)
    - WASD (4 bytes)

6 - game state package
    - header (byte)
      tick (uint)
    - numInputs (byte) (currently always sending state of all players, if optimizing in the future then change to only active players states)
    - states (variable)
        * playerID (byte)
        * position (3 floats)
        * rotation (4 floats)

7 - Time sync request (from client)
    - header (byte)

8 - Time sync (from server)
    - header (byte)
    - server tick on reciept (uint)
    - server time on reciept in milliseconds (double)

*/

public struct NetworkHeaders
{
    public const byte HANDSHAKE = 1;
    public const byte NEW_CONNECTION = 2;
    public const byte DISCONNECTION = 3;
    public const byte CLIENT_INPUTS = 4;
    public const byte SERVER_INPUTS = 5;
    public const byte GAME_STATE_ROLLBACK = 6;
    public const byte TIME_REQUEST = 7;
    public const byte TIME_SYNC = 8;
}

namespace RatNet
{
    public class ServerConnection : MonoBehaviour
    {
        public static ServerConnection Instance;
        //The main driver, how we will interact with netcode
        NetworkDriver m_Driver;
        //The list of all connections
        public NativeList<NetworkConnection> m_Connections;
        NativeList<PlayerInput> recievedInputs;
        uint inputsThisFrame = 0;

        [SerializeField] public bool useTrafficSimulator;
        [SerializeField] public int simulatedDelayMs = 75;
        [SerializeField] public int packetDropPercent = 47;

        bool[] playersInGame = new bool[ClientRollback.MAX_PLAYERS];

        NetworkPipeline rollbackPipeline;
        NetworkPipeline rpcPipeline;

        public uint realtimeTick = 0;
        public byte playerCount = 0;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            var simSettings = new NetworkSettings();
            simSettings.WithSimulatorStageParameters(100, 1472, ApplyMode.AllPackets, simulatedDelayMs, simulatedDelayMs / 3, 0, packetDropPercent);

            //Initialize driver and connection list
            if(useTrafficSimulator)
            m_Driver = NetworkDriver.Create(simSettings);
            else
            m_Driver = NetworkDriver.Create();
            
            recievedInputs = new NativeList<PlayerInput>(ClientRollback.MAX_PLAYERS * 4, Allocator.Persistent);
            m_Connections = new NativeList<NetworkConnection>(ClientRollback.MAX_PLAYERS, Allocator.Persistent);

            
            //Create pipelines
            //Pipeline for frequent data such as inputs and game state
            if(!useTrafficSimulator)
            {
                rollbackPipeline = m_Driver.CreatePipeline(
                typeof(FragmentationPipelineStage));
            }
            else
            {
                rollbackPipeline = m_Driver.CreatePipeline(
                typeof(FragmentationPipelineStage), typeof(SimulatorPipelineStage));
            }
            //For occasional messages with high importance (join/disconnect/whole-game events, etc.)
            rpcPipeline = m_Driver.CreatePipeline(
            typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));

            
            var endpoint = NetworkEndpoint.AnyIpv4.WithPort(7777);
            if (m_Driver.Bind(endpoint) != 0)
            {
                Debug.LogError("Failed to bind to port 7777.");
                return;
            }
            m_Driver.Listen();
        }

        void OnDestroy()
        {
            if (m_Driver.IsCreated)
            {
                m_Driver.Dispose();
                m_Connections.Dispose();
                recievedInputs.Dispose();
            }
        }

        void FixedUpdate()
        {
            realtimeTick++;
        }

        void Update()
        {
            //if(currentTick >= 1250) Debug.Log("AJSDNSKAJDNASKLBFLHDSAFHDSKLGSDKJHBFBLAKF");

            m_Driver.ScheduleUpdate().Complete();

            // Clean up connections.
            for (int i = 0; i < m_Connections.Length; i++)
            {
                if (!m_Connections[i].IsCreated)
                {
                    m_Connections.RemoveAtSwapBack(i);
                    i--;
                }
            }

            #region New Connections

            NetworkConnection c;
            while ((c = m_Driver.Accept()) != default)
            {
                m_Connections.Add(c);
                Debug.Log("Accepted a connection.");
                playerCount++;

                //Find valid ID for new player and send it to them
                int id = m_Connections.IndexOf(c) + 1;

                //Send out id to player and a connection event to all other clients
                for(int i = 0; i < m_Connections.Length; i++)
                {
                    if(id == i + 1)
                    {
                        m_Driver.BeginSend(rpcPipeline, m_Connections[i], out var handshakeWriter);
                        handshakeWriter.WriteByte(NetworkHeaders.HANDSHAKE); 
                        handshakeWriter.WriteByte((byte)id);  
                        handshakeWriter.WriteUInt(realtimeTick);  
                        for(int j = 0; j < m_Connections.Length; j++)
                        {
                            handshakeWriter.WriteByte(m_Connections[j] == default? (byte)0 : (byte)1);  
                        }            
                        m_Driver.EndSend(handshakeWriter);
                    }
                    else if(m_Connections[i] != default)
                    {
                        m_Driver.BeginSend(rpcPipeline, m_Connections[i], out var connectWriter);
                        connectWriter.WriteByte(NetworkHeaders.NEW_CONNECTION); 
                        connectWriter.WriteByte((byte)id);              
                        m_Driver.EndSend(connectWriter);
                    }
                }
            }

            # endregion
            
            #region Recieve Events

            //Perform actions on each connection
            for (int i = 0; i < m_Connections.Length; i++)
            {
                DataStreamReader stream;
                NetworkEvent.Type cmd;
                while ((cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream)) != NetworkEvent.Type.Empty)
                {
                    if (cmd == NetworkEvent.Type.Data)
                    {
                        byte header = stream.ReadByte();
                        if(header == NetworkHeaders.CLIENT_INPUTS)
                        {
                            inputsThisFrame++;
                            recievedInputs.Add(new PlayerInput
                            {
                                /*
                                4 - inputs from client
                                    - header (byte)
                                    - playerID (byte)
                                    - tick (uint)
                                    - WASD (4 bytes)
                                */
                                predicted = false,
                                id = stream.ReadByte(),
                                tick = stream.ReadUInt(),
                                W = stream.ReadByte() == 0? false : true,
                                A = stream.ReadByte() == 0? false : true,
                                S = stream.ReadByte() == 0? false : true,
                                D = stream.ReadByte() == 0? false : true,
                            });
                        }
                        else if (header == NetworkHeaders.TIME_REQUEST)
                        {
                            m_Driver.BeginSend(rpcPipeline, m_Connections[i], out var timeWriter);
                            timeWriter.WriteByte(NetworkHeaders.TIME_SYNC); 
                            timeWriter.WriteUInt(realtimeTick);
                            timeWriter.WriteDouble((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds);
                            m_Driver.EndSend(timeWriter);
                        }
                    }
                    else if (cmd == NetworkEvent.Type.Disconnect)
                    {
                        Debug.Log("Client disconnected from the server.");
                        playerCount--;

                        //Send disconnect event to all players
                        for(int j = 0; j < m_Connections.Length; j++)
                        {
                            if(m_Connections[j] != default)
                            {
                                m_Driver.BeginSend(rpcPipeline, m_Connections[j], out var disconnectWriter);
                                disconnectWriter.WriteByte(NetworkHeaders.DISCONNECTION); 
                                disconnectWriter.WriteByte((byte)(i + 1));
                                m_Driver.EndSend(disconnectWriter);
                            }
                        }

                        m_Connections[i] = default;
                        break;
                    }

                    
                }
            }

            #endregion

            #region Send Inputs

            for (int i = 0; i < m_Connections.Length; i++)
            {
                //Send out inputs to every client (EXCEPT the one that sent them)
                /*
                5 - inputs from server
                    - header (byte)
                    - playerID (byte)
                    - tick (uint)
                    - WASD (4 bytes)
                */
                if(m_Connections[i] == default) continue;

                for(int j = 0; j < inputsThisFrame; j++)
                {
                    if(recievedInputs[j].id == i + 1) continue;

                    m_Driver.BeginSend(rollbackPipeline, m_Connections[i], out var inputWriter);
                    inputWriter.WriteByte(NetworkHeaders.SERVER_INPUTS);
                    inputWriter.WriteByte(recievedInputs[j].id);
                    inputWriter.WriteUInt(recievedInputs[j].tick);
                    inputWriter.WriteByte(recievedInputs[j].W? (byte)1 : (byte)0);
                    inputWriter.WriteByte(recievedInputs[j].A? (byte)1 : (byte)0);
                    inputWriter.WriteByte(recievedInputs[j].S? (byte)1 : (byte)0);
                    inputWriter.WriteByte(recievedInputs[j].D? (byte)1 : (byte)0);
                    m_Driver.EndSend(inputWriter);
                }
            }

            #endregion

            PlayerInput[] newInputs = new PlayerInput[inputsThisFrame];
            for(int i = 0; i < inputsThisFrame; i++)
            {
                newInputs[i] = recievedInputs[i];
            }

            uint newGameTick = ServerSimulation.Instance.ProcessArrivedInput(newInputs);
            
            if(newGameTick != 0)
            {
                for (int i = 0; i < m_Connections.Length; i++)
                {
                    if(m_Connections[i] == default) continue;
                    

                    m_Driver.BeginSend(rpcPipeline, m_Connections[i], out var stateWriter);
                    stateWriter.WriteByte(NetworkHeaders.GAME_STATE_ROLLBACK);
                    stateWriter.WriteUInt(newGameTick);

                    for(int j = 0; j < playerCount; j++)
                    {
                        if(m_Connections[j] == default) continue; 
                        stateWriter.WriteByte((byte)j);
                        stateWriter.WriteFloat(ServerSimulation.Instance.tanks[j].position.x);
                        stateWriter.WriteFloat(ServerSimulation.Instance.tanks[j].position.y);
                        stateWriter.WriteFloat(ServerSimulation.Instance.tanks[j].position.z);

                        stateWriter.WriteFloat(ServerSimulation.Instance.tanks[j].rotation.x);
                        stateWriter.WriteFloat(ServerSimulation.Instance.tanks[j].rotation.y);
                        stateWriter.WriteFloat(ServerSimulation.Instance.tanks[j].rotation.z); 
                        stateWriter.WriteFloat(ServerSimulation.Instance.tanks[j].rotation.w);
                    }
                    m_Driver.EndSend(stateWriter);
                }
            }

            inputsThisFrame = 0;
            recievedInputs.Clear();
        }
    }
}