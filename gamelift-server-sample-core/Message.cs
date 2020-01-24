using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace gamelift_server_sample_core
{
    [DataContract]
    public class Message
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Body { get; set; }

        public static Message FromString(string json)
        {
            var obj = new Message();
            var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var ser = new DataContractJsonSerializer(obj.GetType());
            obj = ser.ReadObject(ms) as Message;
            ms.Close();
            return obj;
        }

        public string Serialize()
        {
            var serializer = new DataContractJsonSerializer(typeof(Message));
            var stream = new MemoryStream();
            serializer.WriteObject(stream, this);
            var json = stream.ToArray();
            return Encoding.UTF8.GetString(json, 0, json.Length);
        }
    }
}