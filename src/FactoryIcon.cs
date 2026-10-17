using HarmonyLib;
using UnityEngine;

namespace SprayFactory
{
    internal static class FactoryIcon
    {
        private static Sprite sprite;
        private static Texture2D texture;

        internal static void Apply()
        {
            if(sprite==null)
            {
                var source=LDB.items.Select(2313).iconSprite;
                var rect=source.textureRect;
                var target=RenderTexture.GetTemporary(80,80,0,RenderTextureFormat.ARGB32);
                var previous=RenderTexture.active;
                try
                {
                    Graphics.Blit(source.texture,target,new Vector2(rect.width/source.texture.width,rect.height/source.texture.height),
                        new Vector2(rect.x/source.texture.width,rect.y/source.texture.height));
                    RenderTexture.active=target;
                    // The native icon atlas copies an 80-pixel ARGB32 texture for each sprite.
                    texture=new Texture2D(80,80,TextureFormat.ARGB32,false) {name="spray-factory-icon"};
                    texture.ReadPixels(new Rect(0,0,80,80),0,0);
                    var pixels=texture.GetPixels();
                    for(int i=0;i<pixels.Length;i++)
                    {
                        var pixel=pixels[i];
                        float value=pixel.a>0?pixel.grayscale:0;
                        pixels[i]=new Color(value,value,value,pixel.a);
                    }
                    texture.SetPixels(pixels);texture.Apply();
                    sprite=Sprite.Create(texture,new Rect(0,0,80,80),source.pivot/source.rect.size,source.pixelsPerUnit);
                    sprite.name="spray-factory-icon";
                }
                finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
            }
            AccessTools.Field(typeof(ItemProto),"_iconSprite").SetValue(LDB.items.Select(Prototypes.ItemId),sprite);
            LDB.models.Select(Prototypes.ModelId).OverrideIconSprite(sprite);
        }

        internal static void Clear()
        {
            if(sprite!=null) Object.Destroy(sprite);
            if(texture!=null) Object.Destroy(texture);
            sprite=null;texture=null;
        }
    }
}
