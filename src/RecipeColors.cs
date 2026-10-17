using System.Collections.Generic;
using UnityEngine;

namespace SprayFactory
{
    internal static class RecipeColors
    {
        private static readonly Dictionary<int,Color> colors=new Dictionary<int,Color>();
        private static readonly Color Neutral=new Color(0.28f,0.28f,0.28f,0.22f);
        internal static Color ForRecipe(int recipeId)
        {
            var recipe=LDB.recipes.Select(recipeId);
            return recipe!=null && recipe.Results.Length>0 ? ForItem(recipe.Results[0]) : Neutral;
        }
        internal static Color ForItem(int itemId)
        {
            if(colors.TryGetValue(itemId,out var color)) return color;
            var sprite=LDB.items.Select(itemId)?.iconSprite;
            color=sprite==null?Neutral:Sample(sprite);
            colors[itemId]=color;return color;
        }
        private static Color Sample(Sprite sprite)
        {
            const int size=32;
            var target=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGB32);
            var previous=RenderTexture.active;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            try {
                var rect=sprite.textureRect;var source=sprite.texture;
                Graphics.Blit(source,target,new Vector2(rect.width/source.width,rect.height/source.height),new Vector2(rect.x/source.width,rect.y/source.height));
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,size,size),0,0);texture.Apply();
                var weights=new float[12];var sums=new Vector3[12];float grayWeight=0;
                foreach(var pixel in texture.GetPixels()) {
                    Color.RGBToHSV(pixel,out float hue,out float saturation,out float value);
                    if(pixel.a<0.5f || value<0.15f) continue;
                    grayWeight+=pixel.a;
                    if(saturation<0.2f) continue;
                    int bin=Mathf.Min(11,(int)(hue*12));float weight=pixel.a*saturation*saturation*value;
                    weights[bin]+=weight;sums[bin]+=new Vector3(pixel.r,pixel.g,pixel.b)*weight;
                }
                int best=0;for(int i=1;i<weights.Length;i++) if(weights[i]>weights[best]) best=i;
                if(grayWeight<=0 || weights[best]<=grayWeight*0.015f) return Neutral;
                var average=sums[best]/weights[best];float scale=0.35f/Mathf.Max(average.x,Mathf.Max(average.y,average.z));
                return new Color(average.x*scale,average.y*scale,average.z*scale,0.22f);
            } finally {RenderTexture.active=previous;Object.Destroy(texture);RenderTexture.ReleaseTemporary(target);}
        }
        internal static void Clear() {colors.Clear();}
    }
}
