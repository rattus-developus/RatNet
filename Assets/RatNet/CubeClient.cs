using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;

namespace RatNet
{
    public class CubeClient : MonoBehaviour
    {
        NetworkDriver m_Driver;
        NetworkConnection m_Connection;

        float tickTimer = 0f;
        float tps = 60f;

        [SerializeField] bool active;
        [SerializeField] Transform cubeTran;

        void Start()
        {
            if(!active) return;

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
            if(!active) return;
            tickTimer += Time.deltaTime;
            if(tickTimer < 1 / tps) return;
            
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
                }
                else if (cmd == NetworkEvent.Type.Data)
                {
                    float x = stream.ReadFloat();
                    float y = stream.ReadFloat();
                    float z = stream.ReadFloat();
                    cubeTran.position = new Vector3(x, y, z);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client got disconnected from server.");
                    m_Connection = default;
                }
            }
        }
    }
}