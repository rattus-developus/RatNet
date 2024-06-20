using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;

namespace RatNet
{
    public class HelloServer : MonoBehaviour
    {
        //The main driver, how we will interact with netcode
        NetworkDriver m_Driver;
        //The list of all connections
        NativeList<NetworkConnection> m_Connections;

        void Start()
        {
            //Initialize blank driver and connection list
            m_Driver = NetworkDriver.Create();
            m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

            //Create a network endpoint that points to all IP adresses
            var endpoint = NetworkEndpoint.AnyIpv4.WithPort(7777);
            //If we can successfuly bind the the port 7777, we begin to listen for new connections
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
            }
        }

        void Update ()
        {
            //This updates the main driver on a single thread.
            //Try here for multi-threading: https://docs.unity3d.com/Packages/com.unity.transport@2.2/manual/client-server-jobs.html
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

            // Accept new connections.
            NetworkConnection c;
            while ((c = m_Driver.Accept()) != default)
            {
                m_Connections.Add(c);
                Debug.Log("Accepted a connection.");
            }
            
            //For each connection, read any data messafes using DataStreamReader
            //Note: There is also a PopEvent method that returns the first event for any connection. The connection is then returned as an out parameter
            for (int i = 0; i < m_Connections.Length; i++)
            {
                DataStreamReader stream;
                NetworkEvent.Type cmd;
                while ((cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream)) != NetworkEvent.Type.Empty)
                {
                    //Starting with the data event
                    if (cmd == NetworkEvent.Type.Data)
                    {
                        uint number = stream.ReadUInt();
                        Debug.Log($"Got {number} from a client, adding 2 to it.");

                        number += 2;

                        //Begin a send on the driver, which will give us a DataStreamWriter to write our data to
                        //Null pipeline is used here, which is the "unreliable" pipeline. Others can be specified,
                        //See: https://docs.unity3d.com/Packages/com.unity.transport@2.2/manual/pipelines-usage.html
                        m_Driver.BeginSend(NetworkPipeline.Null, m_Connections[i], out var writer);
                        //Write data to DataStreamRider
                        writer.WriteUInt(number);
                        //End the writing and schedule for sending                        
                        m_Driver.EndSend(writer);
                    }
                    else if (cmd == NetworkEvent.Type.Disconnect)
                    {
                        Debug.Log("Client disconnected from the server.");
                        m_Connections[i] = default;
                        break;
                    }
                }
            }
        }
    }
}