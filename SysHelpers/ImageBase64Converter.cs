using Newtonsoft.Json;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace RockSnifferLib.SysHelpers
{
    public class ImageBase64Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Image);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return Image.FromStream(new MemoryStream(Convert.FromBase64String(reader.ReadAsString())));
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var img = (Image)value;

            using (var ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Jpeg);
                writer.WriteValue(ms.ToArray());
            }
        }
    }
}
