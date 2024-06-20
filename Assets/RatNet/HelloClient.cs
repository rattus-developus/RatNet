using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;

namespace RatNet
{
    public class HelloClient : MonoBehaviour
    {
        NetworkDriver m_Driver;
        NetworkConnection m_Connection;

        void Start()
        {
            m_Driver = NetworkDriver.Create();
            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(7777);
            m_Connection = m_Driver.Connect(endpoint);
        }

        void OnDestroy()
        {
            if (m_Driver.IsCreated)
            {
                m_Driver.Dispose();
            }
        }

        void Update()
        {
            m_Driver.ScheduleUpdate().Complete();

            //Wait until connection object is succesfully created (not necessarily connected to server)
            if (!m_Connection.IsCreated)
            {
                return;
            }
            
            Unity.Collections.DataStreamReader stream;
            NetworkEvent.Type cmd;
            //The PopEvent here is the single-connection version of the one on the server
            while ((cmd = m_Connection.PopEvent(m_Driver, out stream)) != NetworkEvent.Type.Empty)
            {
                //A Connect event signals a successful connected to the server
                if (cmd == NetworkEvent.Type.Connect)
                {
                    Debug.Log("We are now connected to the server.");

                    uint value = 1;
                    m_Driver.BeginSend(m_Connection, out var writer);
                    writer.WriteUInt(value);
                    m_Driver.EndSend(writer);
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    uint value = stream.ReadUInt();
                    Debug.Log($"Got the value {value} back from the server.");

                    m_Connection.Disconnect(m_Driver);
                    //Note: A good pattern is to always set your NetworkConnection to default to avoid stale references.
                    m_Connection = default;
                }
                /*
                Note: If you were to close the connection before popping the Disconnect event
                (say you're closing it in response to a Data event),
                make sure to pop all remaining events for that connection anyway. 
                Otherwise an error will be printed on the next update about resetting the event queue while there were pending events.
                */
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client got disconnected from server.");
                    m_Connection = default;
                }
            }
        }
    }
}