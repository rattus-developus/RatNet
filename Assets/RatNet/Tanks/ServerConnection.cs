using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;

/* To Do:
- Set up client-server handshake with ID (ensure no data is sent besides ID until handshake is done)

*/

namespace RatNet
{
    public class ServerConnection : MonoBehaviour
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
            
            //Perform actions on each connection
            for (int i = 0; i < m_Connections.Length; i++)
            {
                DataStreamReader stream;
                NetworkEvent.Type cmd;
                while ((cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream)) != NetworkEvent.Type.Empty)
                {
                    if (cmd == NetworkEvent.Type.Data)
                    {
                        //Gather all recieved inputs
                    }
                    else if (cmd == NetworkEvent.Type.Disconnect)
                    {
                        Debug.Log("Client disconnected from the server.");
                        m_Connections[i] = default;
                        break;
                    }
                }
            }

            for (int i = 0; i < m_Connections.Length; i++)
            {
                //Send out inputs to every client (EXCEPT the one that sent them)
            }
        }
    }
}