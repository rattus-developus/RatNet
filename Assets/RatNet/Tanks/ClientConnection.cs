using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport; 

/* To Do:
- Set up client-server handshake with ID (ensure no data is sent besides ID until handshake is done)

*/

namespace RatNet
{
    public class ClientConnection : MonoBehaviour
    {
        //Global singleton reference 
        public static ClientConnection Instance;

        NetworkDriver m_Driver;
        NetworkConnection m_Connection;

        //The local player id ranging from 1 --> MAX_PLAYERS
        public byte localID = 0;

        void Awake()
        {
            Instance = this;
        }

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

        void FixedUpdate()
        {
            /*
            PlayerInput[] dummyInputs = new PlayerInput[1];

            dummyInputs[0] = new PlayerInput
            {
                predicted = false,
                tick = ClientRollback.Instance.currentTick,
                id = 2,
                W = Input.GetKey(KeyCode.UpArrow),
                A = Input.GetKey(KeyCode.LeftArrow),
                S = Input.GetKey(KeyCode.DownArrow),
                D = Input.GetKey(KeyCode.RightArrow),
            };

            ClientRollback.Instance.Simulate(dummyInputs);

            */

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
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Client got disconnected from server.");
                    m_Connection = default;
                }
            }

            //Call Rollback simulate here

            //Send most recent inputs here
        }
    }
}