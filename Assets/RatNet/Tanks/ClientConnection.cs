using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using System;

/* To Do:
- Set up client-server handshake with ID (ensure no data is sent besides ID until handshake is done)

*/

namespace RatNet
{
    public class ClientConnection : MonoBehaviour
    {
        [SerializeField] bool syncTime;

        //Global singleton reference 
        public static ClientConnection Instance;

        NetworkDriver m_Driver;
        NetworkConnection m_Connection;

        //The local player id ranging from 1 --> MAX_PLAYERS
        public byte localID = 0;

        public bool[] activePlayers = new bool[ClientRollback.MAX_PLAYERS];
        NativeList<PlayerInput> recievedInputs;
        uint inputsThisTick = 0;

        NetworkPipeline rollbackPipeline;
        NetworkPipeline rpcPipeline;
        

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            var simSettings = new NetworkSettings();
            simSettings.WithSimulatorStageParameters(100, 1472, ApplyMode.AllPackets, ServerConnection.Instance.simulatedDelayMs, ServerConnection.Instance.simulatedDelayMs / 3, default, 1);

            //Initialize driver and connection list
            if(ServerConnection.Instance.useTrafficSimulator)
            m_Driver = NetworkDriver.Create(simSettings);
            else
            m_Driver = NetworkDriver.Create();

            //Create pipelines
            //Pipeline for frequent data such as inputs and game state
            if(!ServerConnection.Instance.useTrafficSimulator)
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


            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(7777);
            m_Connection = m_Driver.Connect(endpoint);
            recievedInputs = new NativeList<PlayerInput>(ClientRollback.MAX_PLAYERS * 4, Allocator.Persistent);
        }

        void OnDestroy()
        {
            if (m_Driver.IsCreated)
            {
                m_Driver.Dispose();
                recievedInputs.Dispose();
            }
        }

        void FixedUpdate()
        {
            m_Driver.ScheduleUpdate().Complete();

            //Wait until connection object is succesfully created (not necessarily connected to server)
            if (!m_Connection.IsCreated) return;
            
            Unity.Collections.DataStreamReader stream;
            NetworkEvent.Type cmd;
            //The PopEvent here is the single-connection version of the one on the server
            while ((cmd = m_Connection.PopEvent(m_Driver, out stream)) != NetworkEvent.Type.Empty)
            {
                //A Connect event signals a successful connected to the server
                if (cmd == NetworkEvent.Type.Connect)
                {
                    Debug.Log("We are now connected to the server.");
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    //Each struct of player input will be recieved here along with the player ID and tick
                    //Gather it all into an array (order doesn't matter) and sendit to the Rollback Class
                    byte header = stream.ReadByte();

                    if(header == NetworkHeaders.HANDSHAKE)
                    {
                        localID = stream.ReadByte();
                        ClientRollback.Instance.currentTick = stream.ReadUInt();
                        for(int i = 0; i < ClientRollback.MAX_PLAYERS; i++)
                        {
                            activePlayers[i] = stream.ReadByte() == 0? false : true;
                        }
                        activePlayers[localID - 1] = true;
                    }
                    else if(header == NetworkHeaders.SERVER_INPUTS)
                    {
                        inputsThisTick++;
                        recievedInputs.Add(new PlayerInput
                        {
                            /*
                            5 - inputs from server
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
                    else if(header == NetworkHeaders.NEW_CONNECTION)
                    {
                        byte id = stream.ReadByte();
                        activePlayers[id - 1] = true;
                    }
                    else if(header == NetworkHeaders.DISCONNECTION)
                    {
                        byte id = stream.ReadByte();
                        activePlayers[id - 1] = false;
                    }
                    else if(header == NetworkHeaders.TIME_SYNC)
                    {
                        uint serverTick = stream.ReadUInt();
                        double localTime = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                        double serverTime = stream.ReadDouble();
                        double timeSinceTickMs = localTime - serverTime;
                        Debug.Log(timeSinceTickMs);
                        ClientRollback.Instance.currentTick = serverTick + (uint)(timeSinceTickMs / (Time.fixedDeltaTime * 1000f));
                    }
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Local Client got disconnected from server.");
                    m_Connection = default;
                }
            }

            /*
            recievedInputs.Add(new PlayerInput
            {
                predicted = false,
                id = localID,
                tick = ClientRollback.Instance.currentTick,
                W = Input.GetKey(KeyCode.W),
                A = Input.GetKey(KeyCode.A),
                S = Input.GetKey(KeyCode.S),
                D = Input.GetKey(KeyCode.D),
            });
            inputsThisTick++;
            */

            PlayerInput[] newInputs = new PlayerInput[inputsThisTick];
            for(int i = 0; i < inputsThisTick; i++)
            {
                newInputs[i] = recievedInputs[i];
            }

            if(localID != 0)
            {
                PlayerInput localInput = ClientRollback.Instance.Simulate(newInputs);
                m_Driver.BeginSend(rollbackPipeline, m_Connection, out var inputWriter);
                inputWriter.WriteByte(NetworkHeaders.CLIENT_INPUTS);
                inputWriter.WriteByte(localInput.id);
                inputWriter.WriteUInt(localInput.tick);
                inputWriter.WriteByte(localInput.W? (byte)1 : (byte)0);
                inputWriter.WriteByte(localInput.A? (byte)1 : (byte)0);
                inputWriter.WriteByte(localInput.S? (byte)1 : (byte)0);
                inputWriter.WriteByte(localInput.D? (byte)1 : (byte)0);
                m_Driver.EndSend(inputWriter);
            }

            if(syncTime)
            {
                m_Driver.BeginSend(rollbackPipeline, m_Connection, out var timeReqWriter);
                timeReqWriter.WriteByte(NetworkHeaders.TIME_REQUEST);
                m_Driver.EndSend(timeReqWriter);
                syncTime = false;
            }

            //Send local inputs here
            //Debug.Log(inputsThisTick);
            recievedInputs.Clear();
            inputsThisTick = 0;

            /*
            Debug.Log("------Tick-" + ClientRollback.Instance.currentTick + "------");
            Debug.Log("--P1--");
            for(int i = 0; i < ClientRollback.RB_STATES; i++)
            {
                Debug.Log("Tick " + i + " " + ClientRollback.Instance.inputs[i][0].W);
            }
            */
        }
    }
}