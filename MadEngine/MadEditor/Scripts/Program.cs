// SimpleWpf3DEngine_FilledTextured.cs
// Adds: triangle filling + texture mapping using WriteableBitmap rendering.
// Replaces wireframe with rasterizer. This is a full rewrite of the renderer.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleWpf3DEngine
{
    class Program
    {
        [STAThread]
        static void Main()
        {
            var app = new Application();
            var wnd = new MainWindow(800, 600);
            app.Run(wnd);
        }
    }

    public class MainWindow : Window
    {
        private readonly float _width;
        private readonly float _height;
        private readonly System.Windows.Controls.Image _image;
        private readonly WriteableBitmap _bmp;
        private readonly Scene _scene;
        private readonly Renderer _renderer;
        private readonly DispatcherTimer _timer;

        public MainWindow(float width, float height)
        {
            Title = "Simple WPF 3D Engine - Filled + Textured";
            Width = width;
            Height = height;
            _width = width;
            _height = height;

            _bmp = new WriteableBitmap((int)width, (int)height, 96, 96, PixelFormats.Bgra32, null);
            _image = new System.Windows.Controls.Image { Source = _bmp };
            Content = _image;

            _scene = new Scene();

            var cube = Mesh.CreateTexturedCube(1.2f);
            cube.Position = new Vector3(0, 0, 4);
            cube.Rotation = Vector3.Zero;
            _scene.Meshes.Add(cube);

            _scene.Camera = new Camera
            {
                Position = new Vector3(0, 0, 0),
                Target = new Vector3(0, 0, 1),
                Up = Vector3.UnitY,
                FovDegrees = 60f
            };

            _renderer = new Renderer(_bmp);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();

            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        private void Tick()
        {
            var m = _scene.Meshes[0];
            m.Rotation = new Vector3(m.Rotation.X + 0.01f, m.Rotation.Y + 0.02f, m.Rotation.Z);
            _renderer.Render(_scene);
        }
    }

    // ---------------- Scene -------------------------------------------
    public class Scene
    {
        public List<Mesh> Meshes { get; } = new List<Mesh>();
        public Camera Camera { get; set; } = new Camera();
    }

    // ---------------- Mesh --------------------------------------------
    public class Mesh
    {
        public List<Vector3> Vertices = new();
        public List<Vector2> UVs = new();
        public List<(int a, int b, int c)> Tris = new();
        public Vector3 Position = Vector3.Zero;
        public Vector3 Rotation = Vector3.Zero;
        public Vector3 Scale = Vector3.One;

        public WriteableBitmap Texture;

        public static Mesh CreateTexturedCube(float s)
        {
            var m = new Mesh();
            float h = s / 2f;

            m.Vertices.AddRange(new[]
            {
                new Vector3(-h,-h,-h), new Vector3(h,-h,-h), new Vector3(h,h,-h), new Vector3(-h,h,-h),
                new Vector3(-h,-h,h),  new Vector3(h,-h,h),  new Vector3(h,h,h),  new Vector3(-h,h,h)
            });

            m.UVs.AddRange(new[]
            {
                new Vector2(0,1), new Vector2(1,1), new Vector2(1,0), new Vector2(0,0),
                new Vector2(0,1), new Vector2(1,1), new Vector2(1,0), new Vector2(0,0)
            });

            // 12 tris
            int[] f = {0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1, 3,2,6, 3,6,7, 1,5,6, 1,6,2, 0,3,7, 0,7,4};
            for (int i=0;i<f.Length;i+=3) m.Tris.Add((f[i],f[i+1],f[i+2]));

            // simple checkerboard texture
            m.Texture = MakeCheckerTexture(128, 128);
            return m;
        }

        private static WriteableBitmap MakeCheckerTexture(int w, int h)
        {
            var b = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            int stride = w * 4;
            byte[] px = new byte[h * stride];
            for (int y=0;y<h;y++)
            for (int x=0;x<w;x++)
            {
                bool white = ((x/16 + y/16) % 2)==0;
                int idx = y*stride + x*4;
                byte c = white? (byte)255 : (byte)40;
                px[idx+0]=c; px[idx+1]=c; px[idx+2]=c; px[idx+3]=255;
            }
            b.WritePixels(new Int32Rect(0,0,w,h),px,stride,0);
            return b;
        }
    }

    // ---------------- Camera -------------------------------------------
    public class Camera
    {
        public Vector3 Position;
        public Vector3 Target;
        public Vector3 Up;
        public float FovDegrees = 60;

        public void Basis(out Vector3 right,out Vector3 up,out Vector3 forward)
        {
            forward = Vector3.Normalize(Target - Position);
            right = Vector3.Normalize(Vector3.Cross(forward, Up));
            up = Vector3.Normalize(Vector3.Cross(right, forward));
        }
    }

    // ---------------- Renderer (Rasterizer) -----------------------------
    public class Renderer
    {
        private readonly WriteableBitmap _bmp;
        private readonly int _w;
        private readonly int _h;
        private readonly float[] _zbuf;

        public Renderer(WriteableBitmap bmp)
        {
            _bmp = bmp;
            _w = bmp.PixelWidth;
            _h = bmp.PixelHeight;
            _zbuf = new float[_w*_h];
        }

        public void Render(Scene s)
        {
            Array.Fill(_zbuf, float.MaxValue);
            _bmp.Lock();
            unsafe
            {
                int stride = _bmp.BackBufferStride;
                byte* basePtr = (byte*)_bmp.BackBuffer;

                // clear background
                for (int y=0;y<_h;y++)
                    for(int x=0;x<_w;x++)
                    {
                        var p = basePtr + y*stride + x*4;
                        p[0]=0; p[1]=0; p[2]=0; p[3]=255;
                    }

                var cam = s.Camera;
                cam.Basis(out var r, out var u, out var f);

                foreach (var mesh in s.Meshes)
                {
                    var verts = TransformVerts(mesh, cam, r,u,f);

                    foreach (var tri in mesh.Tris)
                    {
                        var v0 = verts[tri.a];
                        var v1 = verts[tri.b];
                        var v2 = verts[tri.c];

                        if (v0.clip || v1.clip || v2.clip) continue;

                        RasterTriangle(v0, v1, v2, mesh.Texture, basePtr, stride);
                    }
                }
            }
            _bmp.AddDirtyRect(new Int32Rect(0,0,_w,_h));
            _bmp.Unlock();
        }

        private (float x, float y, float z, float u, float v, bool clip)[] TransformVerts(Mesh m, Camera cam, Vector3 r, Vector3 u, Vector3 f)
        {
            var res = new (float,float,float,float,float,bool)[m.Vertices.Count];
            float fov = cam.FovDegrees * (float)Math.PI / 180f;
            float ff = 1f / (float)Math.Tan(fov/2);
            float aspect = (float)_w/_h;

            for (int i=0;i<m.Vertices.Count;i++)
            {
                Vector3 p = m.Vertices[i] * m.Scale;
                p = Rotate(p, m.Rotation);
                p += m.Position;

                Vector3 rel = p - cam.Position;
                float cx = Vector3.Dot(rel, r);
                float cy = Vector3.Dot(rel, u);
                float cz = Vector3.Dot(rel, f);

                bool clip = cz <= 0.01f;
                if (!clip)
                {
                    float nx = (cx * ff/aspect)/cz;
                    float ny = (cy * ff)/cz;
                    float sx = (nx+1)*0.5f*_w;
                    float sy = (1-ny)*0.5f*_h;
                    res[i] = (sx, sy, cz, m.UVs[i].X, m.UVs[i].Y, false);
                }
                else res[i] = (0,0,0,0,0,true);
            }
            return res;
        }

        private Vector3 Rotate(Vector3 p, Vector3 r)
        {
            float cx=MathF.Cos(r.X), sx=MathF.Sin(r.X);
            float cy=MathF.Cos(r.Y), sy=MathF.Sin(r.Y);
            float cz=MathF.Cos(r.Z), sz=MathF.Sin(r.Z);
            p = new Vector3(p.X, p.Y*cx - p.Z*sx, p.Y*sx + p.Z*cx);
            p = new Vector3(p.X*cy + p.Z*sy, p.Y, -p.X*sy + p.Z*cy);
            p = new Vector3(p.X*cz - p.Y*sz, p.X*sz + p.Y*cz, p.Z);
            return p;
        }

        unsafe private void RasterTriangle(
            (float x,float y,float z,float u,float v,bool clip) A,
            (float x,float y,float z,float u,float v,bool clip) B,
            (float x,float y,float z,float u,float v,bool clip) C,
            WriteableBitmap tex, byte* basePtr, int stride)
        {
            if (A.y > B.y) (A,B)=(B,A);
            if (A.y > C.y) (A,C)=(C,A);
            if (B.y > C.y) (B,C)=(C,B);

            void DrawScan(float y, float x1, float z1, float u1, float v1,
                                  float x2, float z2, float u2, float v2)
            {
                int yy = (int)y;
                if (yy<0||yy>=_h) return;

                int xs = (int)Math.Min(x1,x2);
                int xe = (int)Math.Max(x1,x2);
                if (xe<0||xs>=_w) return;
                if (xs<0) xs=0;
                if (xe>_w-1) xe=_w-1;

                float dx = x2 - x1;
                for (int x=xs;x<=xe;x++)
                {
                    float t = dx==0?0: (x - x1)/dx;
                    float z = z1 + (z2-z1)*t;
                    int idx = yy*_w + x;
                    if (z >= _zbuf[idx]) continue;
                    _zbuf[idx]=z;

                    float uu = u1 + (u2-u1)*t;
                    float vv = v1 + (v2-v1)*t;
                    int tx = (int)(uu * tex.PixelWidth);
                    int ty = (int)(vv * tex.PixelHeight);
                    tx = Math.Clamp(tx,0,tex.PixelWidth-1);
                    ty = Math.Clamp(ty,0,tex.PixelHeight-1);

                    unsafe
                    {
                        byte* tpx = (byte*)tex.BackBuffer + ty*tex.BackBufferStride + tx*4;
                        byte b = tpx[0], g=tpx[1], r=tpx[2];
                        byte* p = basePtr + yy*stride + x*4;
                        p[0]=b; p[1]=g; p[2]=r; p[3]=255;
                    }
                }
            }

            float dy1 = B.y - A.y;
            float dy2 = C.y - A.y;
            if (dy2==0) return;

            for (float y=A.y;y<=C.y;y++)
            {
                if (y<B.y)
                {
                    float t1 = dy1==0?0:(y-A.y)/dy1;
                    float t2 = (y-A.y)/dy2;
                    DrawScan(y,
                        A.x+(B.x-A.x)*t1, A.z+(B.z-A.z)*t1, A.u+(B.u-A.u)*t1, A.v+(B.v-A.v)*t1,
                        A.x+(C.x-A.x)*t2, A.z+(C.z-A.z)*t2, A.u+(C.u-A.u)*t2, A.v+(C.v-A.v)*t2);
                }
                else
                {
                    float dy3 = C.y - B.y;
                    float t1 = dy3==0?0:(y-B.y)/dy3;
                    float t2 = (y-A.y)/dy2;
                    DrawScan(y,
                        B.x+(C.x-B.x)*t1, B.z+(C.z-B.z)*t1, B.u+(C.u-B.u)*t1, B.v+(C.v-B.v)*t1,
                        A.x+(C.x-A.x)*t2, A.z+(C.z-A.z)*t2, A.u+(C.u-A.u)*t2, A.v+(C.v-A.v)*t2);
                }
            }
        }
    }
}
