// 
// Radegast Metaverse Client
// Copyright (c) 2009-2011, Radegast Development Team
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Radegast", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id$
//

#region Usings
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Text;
using System.Threading;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using OpenMetaverse;
using OpenMetaverse.Rendering;
using OpenMetaverse.Assets;
using OpenMetaverse.Imaging;
using OpenMetaverse.StructuredData;
#endregion Usings

namespace Radegast.Rendering
{

    public partial class SceneWindow : RadegastForm
    {
        #region Public fields
        /// <summary>
        /// The OpenGL surface
        /// </summary>
        public OpenTK.GLControl glControl = null;

        /// <summary>
        /// Use multi sampling (anti aliasing)
        /// </summary>
        public bool UseMultiSampling = true;

        /// <summary>
        /// Is rendering engine ready and enabled
        /// </summary>
        public bool RenderingEnabled = false;

        /// <summary>
        /// Rednder in wireframe mode
        /// </summary>
        public bool Wireframe = false;

        /// <summary>
        /// Object from up to this distance from us will be rendered
        /// </summary>
        public float DrawDistance = 48f;

        /// <summary>
        /// List of prims in the scene
        /// </summary>
        Dictionary<uint, FacetedMesh> Prims = new Dictionary<uint, FacetedMesh>();

        Dictionary<uint, RenderAvatar> Avatars = new Dictionary<uint, RenderAvatar>();

        #endregion Public fields

        #region Private fields

        Camera Camera;
        Dictionary<UUID, TextureInfo> TexturesPtrMap = new Dictionary<UUID, TextureInfo>();
        RadegastInstance instance;
        MeshmerizerR renderer;
        OpenTK.Graphics.GraphicsMode GLMode = null;
        AutoResetEvent TextureThreadContextReady = new AutoResetEvent(false);
        BlockingQueue<TextureLoadItem> PendingTextures = new BlockingQueue<TextureLoadItem>();

        bool hasMipmap;
        Font HoverTextFont = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Regular);
        Font AvatarTagFont = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold);
        Dictionary<UUID, Bitmap> sculptCache = new Dictionary<UUID, Bitmap>();
        OpenTK.Matrix4 ModelMatrix;
        OpenTK.Matrix4 ProjectionMatrix;
        int[] Viewport = new int[4];
        bool useVBO = true;
        System.Diagnostics.Stopwatch renderTimer;
        double lastFrameTime = 0d;
        double advTimerTick = 0d;
        float minLODFactor = 0.005f;
        float timeToFocus = 0.3f;

        float[] lightPos = new float[] { 128f, 128f, 5000f, 0f };
        float ambient = 0.26f;
        float difuse = 0.27f;
        float specular = 0.20f;
        OpenTK.Vector4 ambientColor;
        OpenTK.Vector4 difuseColor;
        OpenTK.Vector4 specularColor;

        #endregion Private fields

        #region Construction and disposal
        public SceneWindow(RadegastInstance instance)
            : base(instance)
        {
            InitializeComponent();
            Disposed += new EventHandler(frmPrimWorkshop_Disposed);
            AutoSavePosition = true;
            UseMultiSampling = cbAA.Checked = instance.GlobalSettings["use_multi_sampling"];
            cbAA.CheckedChanged += cbAA_CheckedChanged;

            this.instance = instance;

            renderer = new MeshmerizerR();
            renderTimer = new System.Diagnostics.Stopwatch();
            renderTimer.Start();

            // Camera initial setting
            Camera = new Camera();
            InitCamera();

            GLAvatar.loadlindenmeshes2("avatar_lad.xml");

            foreach (VisualParamEx vpe in VisualParamEx.morphParams.Values)
            {
                comboBox_morph.Items.Add(vpe.Name);
            }

            foreach (VisualParamEx vpe in VisualParamEx.drivenParams.Values)
            {
                comboBox_driver.Items.Add(vpe.Name);
            }

            Client.Objects.TerseObjectUpdate += new EventHandler<TerseObjectUpdateEventArgs>(Objects_TerseObjectUpdate);
            Client.Objects.ObjectUpdate += new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
            Client.Objects.ObjectDataBlockUpdate += new EventHandler<ObjectDataBlockUpdateEventArgs>(Objects_ObjectDataBlockUpdate);
            Client.Objects.KillObject += new EventHandler<KillObjectEventArgs>(Objects_KillObject);
            Client.Network.SimChanged += new EventHandler<SimChangedEventArgs>(Network_SimChanged);
            Client.Self.TeleportProgress += new EventHandler<TeleportEventArgs>(Self_TeleportProgress);
            Client.Terrain.LandPatchReceived += new EventHandler<LandPatchReceivedEventArgs>(Terrain_LandPatchReceived);
            //Client.Avatars.AvatarAnimation += new EventHandler<AvatarAnimationEventArgs>(AvatarAnimationChanged);
            Client.Avatars.AvatarAppearance += new EventHandler<AvatarAppearanceEventArgs>(Avatars_AvatarAppearance);
            Instance.Netcom.ClientDisconnected += new EventHandler<DisconnectedEventArgs>(Netcom_ClientDisconnected);
            Application.Idle += new EventHandler(Application_Idle);
        }


        void frmPrimWorkshop_Disposed(object sender, EventArgs e)
        {
            Application.Idle -= new EventHandler(Application_Idle);

            PendingTextures.Close();

            Client.Objects.TerseObjectUpdate -= new EventHandler<TerseObjectUpdateEventArgs>(Objects_TerseObjectUpdate);
            Client.Objects.ObjectUpdate -= new EventHandler<PrimEventArgs>(Objects_ObjectUpdate);
            Client.Objects.ObjectDataBlockUpdate -= new EventHandler<ObjectDataBlockUpdateEventArgs>(Objects_ObjectDataBlockUpdate);
            Client.Objects.KillObject -= new EventHandler<KillObjectEventArgs>(Objects_KillObject);
            Client.Network.SimChanged -= new EventHandler<SimChangedEventArgs>(Network_SimChanged);
            Client.Self.TeleportProgress -= new EventHandler<TeleportEventArgs>(Self_TeleportProgress);
            Client.Terrain.LandPatchReceived -= new EventHandler<LandPatchReceivedEventArgs>(Terrain_LandPatchReceived);
            Instance.Netcom.ClientDisconnected -= new EventHandler<DisconnectedEventArgs>(Netcom_ClientDisconnected);
            //Client.Avatars.AvatarAnimation -= new EventHandler<AvatarAnimationEventArgs>(AvatarAnimationChanged);

            if (instance.Netcom != null)
            {
                Instance.Netcom.ClientDisconnected -= new EventHandler<DisconnectedEventArgs>(Netcom_ClientDisconnected);
            }

            if (glControl != null)
            {
                glControl.Dispose();
            }
            glControl = null;

            lock (sculptCache)
            {
                foreach (var img in sculptCache.Values)
                    img.Dispose();
                sculptCache.Clear();
            }

            lock (Prims) Prims.Clear();
            lock (Avatars) Avatars.Clear();

            TexturesPtrMap.Clear();
            GC.Collect();
        }

        void Application_Idle(object sender, EventArgs e)
        {
            if (glControl != null && !glControl.IsDisposed && RenderingEnabled)
            {
                try
                {
                    while (glControl != null && glControl.IsIdle)
                    {
                        MainRenderLoop();
                        if (instance.MonoRuntime)
                        {
                            Application.DoEvents();
                        }
                    }
                }
                catch (ObjectDisposedException)
                { }
            }
        }
        #endregion Construction and disposal

        #region Network messaage handlers
        void Terrain_LandPatchReceived(object sender, LandPatchReceivedEventArgs e)
        {
            if (e.Simulator.Handle == Client.Network.CurrentSim.Handle)
            {
                TerrainModified = true;
            }
        }

        void Netcom_ClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated || !instance.MonoRuntime)
                {
                    BeginInvoke(new MethodInvoker(() => Netcom_ClientDisconnected(sender, e)));
                }
                return;
            }

            Dispose();
        }

        void Self_TeleportProgress(object sender, TeleportEventArgs e)
        {
            switch (e.Status)
            {
                case TeleportStatus.Progress:
                case TeleportStatus.Start:
                    RenderingEnabled = false;
                    break;

                case TeleportStatus.Cancelled:
                case TeleportStatus.Failed:
                    RenderingEnabled = true;
                    break;

                case TeleportStatus.Finished:
                    ThreadPool.QueueUserWorkItem(sync =>
                    {
                        Thread.Sleep(3000);
                        InitCamera();
                        LoadCurrentPrims();
                        RenderingEnabled = true;
                    });
                    break;
            }
        }

        void Network_SimChanged(object sender, SimChangedEventArgs e)
        {
            ResetTerrain();
            lock (sculptCache)
            {
                foreach (var img in sculptCache.Values)
                    img.Dispose();
                sculptCache.Clear();
            }
            lock (Prims) Prims.Clear();
        }

        void Objects_KillObject(object sender, KillObjectEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            // TODO: there should be really cleanup of resources when removing prims and avatars
            lock (Prims) Prims.Remove(e.ObjectLocalID);
            lock (Avatars) Avatars.Remove(e.ObjectLocalID);
        }

        void Objects_TerseObjectUpdate(object sender, TerseObjectUpdateEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            UpdatePrimBlocking(e.Prim);
        }

        void Objects_ObjectUpdate(object sender, PrimEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            UpdatePrimBlocking(e.Prim);
        }

        void Objects_ObjectDataBlockUpdate(object sender, ObjectDataBlockUpdateEventArgs e)
        {
            if (e.Simulator.Handle != Client.Network.CurrentSim.Handle) return;
            UpdatePrimBlocking(e.Prim);
        }

        void AvatarAnimationChanged(object sender, AvatarAnimationEventArgs e)
        {
     
            // We don't currently have UUID -> RenderAvatar mapping so we need to walk the list
            foreach (RenderAvatar av in Avatars.Values)
            {
                if (av.avatar.ID == e.AvatarID)
                {
                    foreach(Animation anim in e.Animations)
                    {
                        Client.Assets.RequestAsset(anim.AnimationID, AssetType.Animation, false, animRecievedCallback);
                        av.animlist.Add(anim.AnimationID, anim);
                    }
                    break;
                }
            }
        }

        void animRecievedCallback(AssetDownload transfer, Asset asset)
        {
            if (transfer.Success)
            {
                BinBVHAnimationReader b = new BinBVHAnimationReader(asset.AssetData);
                
            }
        }

        void Avatars_AvatarAppearance(object sender, AvatarAppearanceEventArgs e)
        {
               // We don't currently have UUID -> RenderAvatar mapping so we need to walk the list
            foreach (RenderAvatar av in Avatars.Values)
            {
                if (av.avatar.ID == e.AvatarID)
                {
                    av.glavatar.morph(av.avatar);
                }
            }
        }


        #endregion Network messaage handlers

        #region glControl setup and disposal
        public void SetupGLControl()
        {
            RenderingEnabled = false;

            if (glControl != null)
                glControl.Dispose();
            glControl = null;

            GLMode = null;

            try
            {
                if (!UseMultiSampling)
                {
                    GLMode = new OpenTK.Graphics.GraphicsMode(OpenTK.DisplayDevice.Default.BitsPerPixel, 24, 8, 0);
                }
                else
                {
                    for (int aa = 0; aa <= 4; aa += 2)
                    {
                        var testMode = new OpenTK.Graphics.GraphicsMode(OpenTK.DisplayDevice.Default.BitsPerPixel, 24, 8, aa);
                        if (testMode.Samples == aa)
                        {
                            GLMode = testMode;
                        }
                    }
                }
            }
            catch
            {
                GLMode = null;
            }


            try
            {
                if (GLMode == null)
                {
                    // Try default mode
                    glControl = new OpenTK.GLControl();
                }
                else
                {
                    glControl = new OpenTK.GLControl(GLMode);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex.Message, Helpers.LogLevel.Warning, Client);
                glControl = null;
            }

            if (glControl == null)
            {
                Logger.Log("Failed to initialize OpenGL control, cannot continue", Helpers.LogLevel.Error, Client);
                return;
            }

            Logger.Log("Initializing OpenGL mode: " + GLMode.ToString(), Helpers.LogLevel.Info);

            glControl.Paint += glControl_Paint;
            glControl.Resize += glControl_Resize;
            glControl.MouseDown += glControl_MouseDown;
            glControl.MouseUp += glControl_MouseUp;
            glControl.MouseMove += glControl_MouseMove;
            glControl.MouseWheel += glControl_MouseWheel;
            glControl.Load += new EventHandler(glControl_Load);
            glControl.Disposed += new EventHandler(glControl_Disposed);
            glControl.Dock = DockStyle.Fill;
            glControl.VSync = false;
            Controls.Add(glControl);
            glControl.BringToFront();
        }

        void glControl_Disposed(object sender, EventArgs e)
        {
            TextureThreadRunning = false;
            PendingTextures.Close();
            glControl.Paint -= glControl_Paint;
            glControl.Resize -= glControl_Resize;
            glControl.MouseDown -= glControl_MouseDown;
            glControl.MouseUp -= glControl_MouseUp;
            glControl.MouseMove -= glControl_MouseMove;
            glControl.MouseWheel -= glControl_MouseWheel;
            glControl.Load -= new EventHandler(glControl_Load);
            glControl.Disposed -= glControl_Disposed;
        }

        void SetSun()
        {
            ambientColor = new OpenTK.Vector4(ambient, ambient, ambient, difuse);
            difuseColor = new OpenTK.Vector4(difuse, difuse, difuse, difuse);
            specularColor = new OpenTK.Vector4(specular, specular, specular, specular);
            GL.Light(LightName.Light0, LightParameter.Ambient, ambientColor);
            GL.Light(LightName.Light0, LightParameter.Diffuse, difuseColor);
            GL.Light(LightName.Light0, LightParameter.Specular, specularColor);
            GL.Light(LightName.Light0, LightParameter.Position, lightPos);
        }

        void glControl_Load(object sender, EventArgs e)
        {
            try
            {
                GL.ShadeModel(ShadingModel.Smooth);

                GL.Enable(EnableCap.Lighting);
                GL.Enable(EnableCap.Light0);
                SetSun();

                GL.ClearDepth(1.0d);
                GL.Enable(EnableCap.DepthTest);
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceMode.Back);

                // GL.Color() tracks objects ambient and diffuse color
                GL.Enable(EnableCap.ColorMaterial);
                GL.ColorMaterial(MaterialFace.Front, ColorMaterialParameter.AmbientAndDiffuse);

                GL.DepthMask(true);
                GL.DepthFunc(DepthFunction.Lequal);
                GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
                GL.MatrixMode(MatrixMode.Projection);

                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                hasMipmap = GL.GetString(StringName.Extensions).Contains("GL_SGIS_generate_mipmap");
                // Double check if we have mipmap ability
                if (hasMipmap)
                {
                    try
                    {
                        int testID = -1;
                        Bitmap testPic = new Bitmap(1, 1);
                        BitmapData testData = testPic.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        GL.GenTextures(1, out testID);
                        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, 1, 1, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgr, PixelType.UnsignedByte, testData.Scan0);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.GenerateMipmap, 1);
                        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                        testPic.UnlockBits(testData);
                        testPic.Dispose();
                        GL.DeleteTexture(testID);
                    }
                    catch
                    {
                        Logger.DebugLog("Don't have glGenerateMipmap() after all");
                        hasMipmap = false;
                    }
                }

                RenderingEnabled = true;
                // Call the resizing function which sets up the GL drawing window
                // and will also invalidate the GL control
                glControl_Resize(null, null);

                glControl.Context.MakeCurrent(null);
                TextureThreadContextReady.Reset();
                var textureThread = new Thread(() => TextureThread())
                {
                    IsBackground = true,
                    Name = "TextureLoadingThread"
                };
                textureThread.Start();
                TextureThreadContextReady.WaitOne(1000, false);
                glControl.MakeCurrent();
            }
            catch (Exception ex)
            {
                RenderingEnabled = false;
                Logger.Log("Failed to initialize OpenGL control", Helpers.LogLevel.Warning, Client, ex);
            }
        }
        #endregion glControl setup and disposal

        #region glControl paint and resize events
        private void MainRenderLoop()
        {
            if (!RenderingEnabled) return;
            lastFrameTime = renderTimer.Elapsed.TotalSeconds;

            // Something went horribly wrong
            if (lastFrameTime < 0) return;

            // Stopwatch loses resolution if it runs for a long time, reset it
            renderTimer.Reset();
            renderTimer.Start();

            Render(false);

            glControl.SwapBuffers();
        }

        void glControl_Paint(object sender, EventArgs e)
        {
            MainRenderLoop();
        }

        private void glControl_Resize(object sender, EventArgs e)
        {
            if (!RenderingEnabled) return;
            glControl.MakeCurrent();

            GL.ClearColor(0.39f, 0.58f, 0.93f, 1.0f);

            GL.Viewport(0, 0, glControl.Width, glControl.Height);

            GL.PushMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            SetPerspective();

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();
        }
        #endregion glControl paint and resize events

        #region Mouse handling
        bool dragging = false;
        int dragX, dragY, downX, downY;

        private void glControl_MouseWheel(object sender, MouseEventArgs e)
        {
        }

        FacetedMesh RightclickedPrim;
        int RightclickedFaceID;

        private void glControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                downX = dragX = e.X;
                downY = dragY = e.Y;
            }
            else if (e.Button == MouseButtons.Right)
            {
                object picked;
                if (TryPick(e.X, e.Y, out picked, out RightclickedFaceID))
                {
                    if (picked is FacetedMesh)
                    {
                        RightclickedPrim = (FacetedMesh)picked;
                        ctxObjects.Show(glControl, e.X, e.Y);
                    }
                    else if (picked is RenderAvatar)
                    {
                        // TODO: add context menu when clicked on an avatar
                    }
                }
            }

        }

        private void glControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                int deltaX = e.X - dragX;
                int deltaY = e.Y - dragY;
                float pixelToM = 1f / 75f;

                if (e.Button == MouseButtons.Left)
                {
                    // Pan
                    if (ModifierKeys == Keys.Control || ModifierKeys == (Keys.Alt | Keys.Control | Keys.Shift))
                    {
                        Vector3 direction = Camera.Position - Camera.FocalPoint;
                        direction.Normalize();
                        Vector3 vy = direction % new Vector3(0f, 0f, 1f);
                        Vector3 vx = vy % direction;
                        Vector3 vxy = vx * deltaY * pixelToM + vy * deltaX * pixelToM;
                        Camera.Position += vxy;
                        Camera.FocalPoint += vxy;
                    }

                    // Alt-zoom (up down move camera closer to target, left right rotate around target)
                    if (ModifierKeys == Keys.Alt)
                    {
                        Camera.Position += (Camera.Position - Camera.FocalPoint) * deltaY * pixelToM;
                        var dx = -(deltaX * pixelToM);
                        Camera.Position = Camera.FocalPoint + (Camera.Position - Camera.FocalPoint) * new Quaternion(0f, 0f, (float)Math.Sin(dx), (float)Math.Cos(dx));
                    }

                    // Rotate camera in a vertical circle around target on up down mouse movement
                    if (ModifierKeys == (Keys.Alt | Keys.Control))
                    {
                        Camera.Position = Camera.FocalPoint +
                            (Camera.Position - Camera.FocalPoint)
                            * Quaternion.CreateFromAxisAngle((Camera.Position - Camera.FocalPoint) % new Vector3(0f, 0f, 1f), deltaY * pixelToM);
                        var dx = -(deltaX * pixelToM);
                        Camera.Position = Camera.FocalPoint + (Camera.Position - Camera.FocalPoint) * new Quaternion(0f, 0f, (float)Math.Sin(dx), (float)Math.Cos(dx));
                    }

                }

                dragX = e.X;
                dragY = e.Y;
            }
        }

        private void glControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;

                if (e.X == downX && e.Y == downY) // click
                {
                    object clicked;
                    int faceID;
                    if (TryPick(e.X, e.Y, out clicked, out faceID))
                    {
                        if (clicked is FacetedMesh)
                        {
                            FacetedMesh picked = (FacetedMesh)clicked;

                            if (ModifierKeys == Keys.None)
                            {
                                Client.Self.Grab(picked.Prim.LocalID, Vector3.Zero, Vector3.Zero, Vector3.Zero, faceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                                Client.Self.GrabUpdate(picked.Prim.ID, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, faceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                                Client.Self.DeGrab(picked.Prim.LocalID);
                            }
                            else if (ModifierKeys == Keys.Alt)
                            {
                                Camera.TimeToTarget = timeToFocus;
                                Camera.FocalPoint = PrimPos(picked.Prim);
                                Cursor.Position = glControl.PointToScreen(new Point(glControl.Width / 2, glControl.Height / 2));
                            }
                        }
                        else if (clicked is RenderAvatar)
                        {
                            RenderAvatar av = (RenderAvatar)clicked;
                            if (ModifierKeys == Keys.Alt)
                            {
                                Camera.TimeToTarget = timeToFocus;
                                Vector3 pos = PrimPos(av.avatar);
                                pos.Z += 1.5f; // focus roughly on the chest area
                                Camera.FocalPoint = pos;
                                Cursor.Position = glControl.PointToScreen(new Point(glControl.Width / 2, glControl.Height / 2));
                            }
                        }
                    }
                }
            }
        }
        #endregion Mouse handling

        // Switch to ortho display mode for drawing hud
        public void GLHUDBegin()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.Light0);
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0, glControl.Width, 0, glControl.Height, -5, 1);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
        }

        // Switch back to frustrum display mode
        public void GLHUDEnd()
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Lighting);
            GL.Enable(EnableCap.Light0);
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
        }

        public int GLLoadImage(Bitmap bitmap, bool hasAlpha)
        {
            int ret = -1;
            GL.GenTextures(1, out ret);
            GL.BindTexture(TextureTarget.Texture2D, ret);

            Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);

            BitmapData bitmapData =
                bitmap.LockBits(
                rectangle,
                ImageLockMode.ReadOnly,
                hasAlpha ? System.Drawing.Imaging.PixelFormat.Format32bppArgb : System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                hasAlpha ? PixelInternalFormat.Rgba : PixelInternalFormat.Rgb8,
                bitmap.Width,
                bitmap.Height,
                0,
                hasAlpha ? OpenTK.Graphics.OpenGL.PixelFormat.Bgra : OpenTK.Graphics.OpenGL.PixelFormat.Bgr,
                PixelType.UnsignedByte,
                bitmapData.Scan0);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            if (hasMipmap)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.GenerateMipmap, 1);
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }

            bitmap.UnlockBits(bitmapData);
            return ret;
        }

        #region Texture thread
        bool TextureThreadRunning = true;

        void TextureThread()
        {
            OpenTK.INativeWindow window = new OpenTK.NativeWindow();
            OpenTK.Graphics.IGraphicsContext context = new OpenTK.Graphics.GraphicsContext(GLMode, window.WindowInfo);
            context.MakeCurrent(window.WindowInfo);
            TextureThreadContextReady.Set();
            PendingTextures.Open();
            Logger.DebugLog("Started Texture Thread");

            while (window.Exists && TextureThreadRunning)
            {
                window.ProcessEvents();

                TextureLoadItem item = null;

                if (!PendingTextures.Dequeue(Timeout.Infinite, ref item)) continue;

                // Already have this one loaded
                if (item.Data.TextureInfo.TexturePointer != 0) continue;

                if (item.TextureData != null)
                {
                    ManagedImage mi;
                    Image img;
                    OpenJPEG.DecodeToImage(item.TextureData, out mi, out img);
                    Bitmap bitmap = (Bitmap)img;

                    bool hasAlpha;
                    if (bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                    {
                        hasAlpha = true;
                    }
                    else
                    {
                        hasAlpha = false;
                    }

                    item.Data.TextureInfo.HasAlpha = hasAlpha;
                    bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
                    item.Data.TextureInfo.TexturePointer = GLLoadImage(bitmap, hasAlpha);
                    bitmap.Dispose();
                    item.TextureData = null;
                }
            }
            Logger.DebugLog("Texture thread exited");
        }
        #endregion Texture thread

        void LoadCurrentPrims()
        {
            if (!Client.Network.Connected) return;

            ThreadPool.QueueUserWorkItem(sync =>
            {
                Client.Network.CurrentSim.ObjectsPrimitives.FindAll((Primitive root) => root.ParentID == 0).ForEach((Primitive mainPrim) =>
                {
                    UpdatePrimBlocking(mainPrim);
                    Client.Network.CurrentSim.ObjectsPrimitives
                        .FindAll((Primitive child) => child.ParentID == mainPrim.LocalID)
                        .ForEach((Primitive subPrim) => UpdatePrimBlocking(subPrim));
                });

                Client.Network.CurrentSim.ObjectsAvatars.ForEach(delegate(Avatar avatar)
                {
                    UpdatePrimBlocking(avatar);
                    Client.Network.CurrentSim.ObjectsPrimitives
                        .FindAll((Primitive child) => child.ParentID == avatar.LocalID)
                        .ForEach((Primitive attachedPrim) =>
                        {
                            UpdatePrimBlocking(attachedPrim);
                            Client.Network.CurrentSim.ObjectsPrimitives
                                .FindAll((Primitive child) => child.ParentID == attachedPrim.LocalID)
                                .ForEach((Primitive attachedPrimChild) =>
                                {
                                    UpdatePrimBlocking(attachedPrimChild);
                                });
                        });
                });
            });
        }

        private void frmPrimWorkshop_Shown(object sender, EventArgs e)
        {
            SetupGLControl();
            LoadCurrentPrims();
        }

        #region Private methods (the meat)
        private void UpdateCamera()
        {
            if (Client != null)
            {
                Client.Self.Movement.Camera.LookAt(Camera.Position, Camera.FocalPoint);
                //Client.Self.Movement.Camera.Far = (float)Camera.Far;
            }
        }

        void InitCamera()
        {
            Vector3 camPos = Client.Self.SimPosition + new Vector3(-2, 0, 0) * Client.Self.Movement.BodyRotation;
            camPos.Z += 2f;
            Camera.Position = camPos;
            Camera.FocalPoint = Client.Self.SimPosition + new Vector3(5, 0, 0) * Client.Self.Movement.BodyRotation;
            Camera.Zoom = 1.0f;
            Camera.Far = 128.0f;
            Camera.EndMove();
        }

        Vector3 PrimPos(Primitive prim)
        {
            if (prim.ParentID == 0)
            {
                return prim.Position;
            }
            else
            {
                FacetedMesh parent;
                RenderAvatar parentav;
                if (Prims.TryGetValue(prim.ParentID, out parent))
                {
                    if (parent.Prim.ParentID == 0)
                    {
                        return parent.Prim.Position + prim.Position * Matrix4.CreateFromQuaternion(parent.Prim.Rotation);
                    }
                    else
                    {
                        // This is an child prim of an attachment
                        if (Avatars.TryGetValue(parent.Prim.ParentID, out parentav))
                        {
                            var avPos = PrimPos(parentav.avatar);
                            return avPos;
                        }
                        else
                        {
                            return new Vector3(99999f, 99999f, 99999f);
                        }
                    }
                }
                else if (Avatars.TryGetValue(prim.ParentID, out parentav))
                {
                    var avPos = PrimPos(parentav.avatar);

                    return avPos + prim.Position * Matrix4.CreateFromQuaternion(parentav.avatar.Rotation);
                }
                else
                {
                    return new Vector3(99999f, 99999f, 99999f);
                }
            }
        }

        private void SetPerspective()
        {
            float dAspRat = (float)glControl.Width / (float)glControl.Height;
            GluPerspective(50.0f * Camera.Zoom, dAspRat, 0.1f, 1000f);
        }


#pragma warning disable 0612
        OpenTK.Graphics.TextPrinter Printer = new OpenTK.Graphics.TextPrinter(OpenTK.Graphics.TextQuality.High);
#pragma warning restore 0612

        private void RenderStats()
        {
            int posX = glControl.Width - 100;
            int posY = 0;

            // This is a FIR filter known as a MMA or Modified Mean Average, using a 20 point sampling width
            advTimerTick = ((19 * advTimerTick) + lastFrameTime) / 20;

            Printer.Begin();
            Printer.Print(String.Format("FPS {0:000.00}", 1d / advTimerTick), AvatarTagFont, Color.Orange,
                new RectangleF(posX, posY, 100, 50),
                OpenTK.Graphics.TextPrinterOptions.Default, OpenTK.Graphics.TextAlignment.Center);
            Printer.End();
        }

        private void RenderText()
        {
            lock (Avatars)
            {

                GL.Color4(0f, 0f, 0f, 0.6f);

                foreach (RenderAvatar av in Avatars.Values)
                {
                    Vector3 avPos = PrimPos(av.avatar);
                    if (Vector3.Distance(avPos, Camera.Position) > 20f) continue;

                    OpenTK.Vector3 tagPos = RHelp.TKVector3(avPos);
                    tagPos.Z += 2.2f;
                    OpenTK.Vector3 screenPos;
                    if (!Math3D.GluProject(tagPos, ModelMatrix, ProjectionMatrix, Viewport, out screenPos)) continue;

                    string tagText = instance.Names.Get(av.avatar.ID, av.avatar.Name);
                    if (!string.IsNullOrEmpty(av.avatar.GroupName))
                    {
                        tagText = av.avatar.GroupName + "\n" + tagText;
                    }
                    var tSize = Printer.Measure(tagText, AvatarTagFont);

                    // Render tag backround
                    GL.Begin(BeginMode.Quads);
                    float halfWidth = tSize.BoundingBox.Width / 2 + 10;
                    float halfHeight = tSize.BoundingBox.Height / 2 + 5;
                    GL.Vertex2(screenPos.X - halfWidth, screenPos.Y - halfHeight);
                    GL.Vertex2(screenPos.X + halfWidth, screenPos.Y - halfHeight);
                    GL.Vertex2(screenPos.X + halfWidth, screenPos.Y + halfHeight);
                    GL.Vertex2(screenPos.X - halfWidth, screenPos.Y + halfHeight);
                    GL.End();

                    screenPos.Y = glControl.Height - screenPos.Y;
                    screenPos.X -= tSize.BoundingBox.Width / 2;
                    screenPos.Y -= tSize.BoundingBox.Height / 2;

                    if (screenPos.Y > 0)
                    {
                        Printer.Begin();
                        Printer.Print(tagText, AvatarTagFont, Color.Orange,
                            new RectangleF(screenPos.X, screenPos.Y, tSize.BoundingBox.Width, tSize.BoundingBox.Height),
                            OpenTK.Graphics.TextPrinterOptions.Default, OpenTK.Graphics.TextAlignment.Center);
                        Printer.End();
                    }
                }
            }

            lock (Prims)
            {
                int primNr = 0;
                foreach (FacetedMesh mesh in Prims.Values)
                {
                    primNr++;
                    Primitive prim = mesh.Prim;
                    if (!string.IsNullOrEmpty(prim.Text))
                    {
                        string text = System.Text.RegularExpressions.Regex.Replace(prim.Text, "(\r?\n)+", "\n");
                        var newPrimPos = PrimPos(prim);
                        OpenTK.Vector3 primPos = new OpenTK.Vector3(newPrimPos.X, newPrimPos.Y, newPrimPos.Z);
                        var distance = Vector3.Distance(newPrimPos, Camera.Position);

                        // Display hovertext only on objects that are withing 12m of the camera
                        if (distance > 12) continue;

                        primPos.Z += prim.Scale.Z * 0.8f;

                        // Convert objects world position to 2D screen position in pixels
                        OpenTK.Vector3 screenPos;
                        if (!Math3D.GluProject(primPos, ModelMatrix, ProjectionMatrix, Viewport, out screenPos)) continue;
                        screenPos.Y = glControl.Height - screenPos.Y;

                        Printer.Begin();

                        Color color = Color.FromArgb((int)(prim.TextColor.A * 255), (int)(prim.TextColor.R * 255), (int)(prim.TextColor.G * 255), (int)(prim.TextColor.B * 255));

                        var size = Printer.Measure(text, HoverTextFont);
                        screenPos.X -= size.BoundingBox.Width / 2;
                        screenPos.Y -= size.BoundingBox.Height;

                        if (screenPos.Y > 0)
                        {

                            // Shadow
                            if (color != Color.Black)
                            {
                                Printer.Print(text, HoverTextFont, Color.Black, new RectangleF(screenPos.X + 1, screenPos.Y + 1, size.BoundingBox.Width, size.BoundingBox.Height), OpenTK.Graphics.TextPrinterOptions.Default, OpenTK.Graphics.TextAlignment.Center);
                            }
                            // Text
                            Printer.Print(text, HoverTextFont, color, new RectangleF(screenPos.X, screenPos.Y, size.BoundingBox.Width, size.BoundingBox.Height), OpenTK.Graphics.TextPrinterOptions.Default, OpenTK.Graphics.TextAlignment.Center);
                        }

                        Printer.End();
                    }
                }
            }
        }

        #region avatars

        private void AddAvatarToScene(Avatar av)
        {
            lock (Avatars)
            {
                if (Vector3.Distance(PrimPos(av), Client.Self.SimPosition) > 32) return;

                if (Avatars.ContainsKey(av.LocalID))
                {
                    // flag we got an update??
                    updateAVtes(Avatars[av.LocalID]);
                }
                else
                {
                    GLAvatar ga = new GLAvatar();
                    
                    //ga.morph(av);
                    RenderAvatar ra = new Rendering.RenderAvatar();
                    ra.avatar = av;
                    ra.glavatar = ga;
                    updateAVtes(ra);
                    Avatars.Add(av.LocalID, ra);
                    ra.glavatar.morph(av);

                }
            }
        }

        private void updateAVtes(RenderAvatar ra)
        {
            if (ra.avatar.Textures == null)
                return;

            int[] tes = { 8, 9, 10, 11, 19, 20 };

            foreach (int fi in tes)
            {
                Primitive.TextureEntryFace TEF = ra.avatar.Textures.FaceTextures[fi];
                if (TEF == null)
                    continue;

                if (ra.data[fi] == null || ra.data[fi].TextureInfo.TextureID != TEF.TextureID)
                {
                    FaceData data = new FaceData();
                    ra.data[fi] = data;
                    data.TextureInfo.TextureID = TEF.TextureID;

                    DownloadTexture(new TextureLoadItem()
                    {
                        Data = data,
                        Prim = ra.avatar,
                        TeFace = ra.avatar.Textures.FaceTextures[fi]
                    });
                }
            }
        }

        private void RenderAvatarsSkeleton(RenderPass pass)
        {
            foreach (RenderAvatar av in Avatars.Values)
            {
                // Individual prim matrix
                GL.PushMatrix();

                // Prim roation and position
                Vector3 pos = av.avatar.Position;
                pos.X += 1;
                GL.MultMatrix(Math3D.CreateTranslationMatrix(pos));
                GL.MultMatrix(Math3D.CreateRotationMatrix(av.avatar.Rotation));

                GL.Begin(BeginMode.Lines);

                GL.Color3(1.0, 0.0, 0.0);
                
                foreach (Bone b in av.glavatar.skel.mBones.Values)
                {
                    Vector3 newpos = b.getOffset();

                    if (b.parent != null)
                    {
                        Vector3 parentpos = b.parent.getOffset();
                        GL.Vertex3(parentpos.X,parentpos.Y,parentpos.Z);
                    }
                    else
                    {                       
                        GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    }

                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    //Mark the joints

                   
                    newpos.X += 0.01f;
                    newpos.Y += 0.01f;
                    newpos.Z += 0.01f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.X -= 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                  
                    newpos.Y -= 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.X += 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.Y += 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.Z -= 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.Y -= 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.X -= 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.Y += 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.X += 0.02f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);

                    newpos.Y -= 0.01f;
                    newpos.Z += 0.01f;
                    newpos.X -= 0.01f;
                    GL.Vertex3(newpos.X, newpos.Y, newpos.Z);



                }


                
                GL.Color3(0.0, 1.0, 0.0);

                GL.End();

                GL.PopMatrix();
            }
        }

        private void RenderAvatars(RenderPass pass)
        {
            lock (Avatars)
            {
                GL.EnableClientState(ArrayCap.VertexArray);
                GL.EnableClientState(ArrayCap.TextureCoordArray);
                GL.EnableClientState(ArrayCap.NormalArray);

                int avatarNr = 0;
                foreach (RenderAvatar av in Avatars.Values)
                {
                    avatarNr++;

                    if (av.glavatar._meshes.Count > 0)
                    {
                        int faceNr = 0;
                        foreach (GLMesh mesh in av.glavatar._meshes.Values)
                        {
                            faceNr++;
                            if (!av.glavatar._showSkirt && mesh.Name == "skirtMesh")
                                continue;

                            if (mesh.Name == "hairMesh") // Don't render the hair mesh for the moment
                                continue;

                            GL.Color3(1f, 1f, 1f);

                            // Individual prim matrix
                            GL.PushMatrix();

                            // Prim roation and position
                            //GL.MultMatrix(Math3D.CreateTranslationMatrix(av.avatar.Position));
                            //GL.MultMatrix(Math3D.CreateRotationMatrix(av.avatar.Rotation));
                            GL.MultMatrix(Math3D.CreateSRTMatrix(new Vector3(1,1,1),av.avatar.Rotation,av.avatar.Position));

                            // Special case for eyeballs we need to offset the mesh to the correct position
                            // We have manually added the eyeball offset based on the headbone when we
                            // constructed the meshes, but why are the position offsets we got when loading
                            // the other meshes <0,7,0> ?
                            if (mesh.Name == "eyeBallLeftMesh")
                            {
                                // Mesh roation and position
                                GL.MultMatrix(Math3D.CreateTranslationMatrix(av.glavatar.skel.getOffset("mEyeLeft")));
                                GL.MultMatrix(Math3D.CreateRotationMatrix(av.glavatar.skel.getRotation("mEyeLeft")));
                            }
                            if (mesh.Name == "eyeBallRightMesh")
                            {
                                // Mesh roation and position
                                GL.MultMatrix(Math3D.CreateTranslationMatrix(av.glavatar.skel.getOffset("mEyeRight")));
                                GL.MultMatrix(Math3D.CreateRotationMatrix(av.glavatar.skel.getRotation("mEyeRight")));
                            }



                            //Should we be offsetting the base meshs at all?
                            //if (mesh.Name == "headMesh")
                            //{
                            //    GL.MultMatrix(Math3D.CreateTranslationMatrix(av.glavatar.skel.getDeltaOffset("mHead")));
                            //}


                            //Gl.glTranslatef(mesh.Position.X, mesh.Position.Y, mesh.Position.Z);

                            GL.Rotate(mesh.RotationAngles.X, 1f, 0f, 0f);
                            GL.Rotate(mesh.RotationAngles.Y, 0f, 1f, 0f);
                            GL.Rotate(mesh.RotationAngles.Z, 0f, 0f, 1f);

                            GL.Scale(mesh.Scale.X, mesh.Scale.Y, mesh.Scale.Z);

                            if (pass == RenderPass.Picking)
                            {
                                GL.Disable(EnableCap.Texture2D);

                                for (int i = 0; i < av.data.Length; i++)
                                {
                                    if (av.data[i] != null)
                                    {
                                        av.data[i].PickingID = avatarNr;
                                    }
                                }
                                byte[] primNrBytes = Utils.Int16ToBytes((short)avatarNr);
                                byte[] faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)faceNr, 254 };
                                GL.Color4(faceColor);
                            }
                            else
                            {
                                if (av.data[mesh.teFaceID] == null)
                                {
                                    GL.Disable(EnableCap.Texture2D);
                                }
                                else
                                {
                                    if (mesh.teFaceID != 0)
                                    {
                                        GL.Enable(EnableCap.Texture2D);
                                        GL.BindTexture(TextureTarget.Texture2D, av.data[mesh.teFaceID].TextureInfo.TexturePointer);
                                    }
                                    else
                                    {
                                        GL.Disable(EnableCap.Texture2D);
                                    }
                                }
                            }

                            GL.TexCoordPointer(2, TexCoordPointerType.Float, 0, mesh.RenderData.TexCoords);
                            GL.VertexPointer(3, VertexPointerType.Float, 0, mesh.RenderData.Vertices);
                            GL.NormalPointer(NormalPointerType.Float, 0, mesh.RenderData.Normals);

                            GL.DrawElements(BeginMode.Triangles, mesh.RenderData.Indices.Length, DrawElementsType.UnsignedShort, mesh.RenderData.Indices);

                            GL.BindTexture(TextureTarget.Texture2D, 0);

                            GL.PopMatrix();

                        }
                    }
                }
                GL.Disable(EnableCap.Texture2D);
                GL.DisableClientState(ArrayCap.NormalArray);
                GL.DisableClientState(ArrayCap.VertexArray);
                GL.DisableClientState(ArrayCap.TextureCoordArray);
                
            }
        }
        #endregion avatars

        #region Terrian
        bool TerrainModified = true;
        float[,] heightTable = new float[256, 256];
        Face terrainFace;
        ushort[] terrainIndices;
        Vertex[] terrainVertices;
        int terrainTexture = -1;
        bool fetchingTerrainTexture = false;
        Bitmap terrainImage = null;
        int terrainVBO = -1;
        int terrainIndexVBO = -1;

        private void ResetTerrain()
        {
            ResetTerrain(true);
        }

        private void ResetTerrain(bool removeImage)
        {
            if (terrainImage != null)
            {
                terrainImage.Dispose();
                terrainImage = null;
            }

            if (terrainVBO != -1)
            {
                GL.DeleteBuffers(1, ref terrainVBO);
                terrainVBO = -1;
            }

            if (terrainIndexVBO != -1)
            {
                GL.DeleteBuffers(1, ref terrainIndexVBO);
                terrainIndexVBO = -1;
            }

            if (removeImage)
            {
                if (terrainTexture != -1)
                {
                    GL.DeleteTexture(terrainTexture);
                    terrainTexture = -1;
                }
            }

            fetchingTerrainTexture = false;
            TerrainModified = true;
        }

        private void UpdateTerrain()
        {
            if (Client.Network.CurrentSim == null || Client.Network.CurrentSim.Terrain == null) return;
            int step = 1;

            for (int x = 0; x < 255; x += step)
            {
                for (int y = 0; y < 255; y += step)
                {
                    float z = 0;
                    int patchNr = ((int)x / 16) * 16 + (int)y / 16;
                    if (Client.Network.CurrentSim.Terrain[patchNr] != null
                        && Client.Network.CurrentSim.Terrain[patchNr].Data != null)
                    {
                        float[] data = Client.Network.CurrentSim.Terrain[patchNr].Data;
                        z = data[(int)x % 16 * 16 + (int)y % 16];
                    }
                    heightTable[x, y] = z;
                }
            }

            terrainFace = renderer.TerrainMesh(heightTable, 0f, 255f, 0f, 255f);
            terrainVertices = terrainFace.Vertices.ToArray();
            terrainIndices = terrainFace.Indices.ToArray();

            TerrainModified = false;
        }

        void UpdateTerrainTexture()
        {
            if (!fetchingTerrainTexture)
            {
                fetchingTerrainTexture = true;
                ThreadPool.QueueUserWorkItem(sync =>
                {
                    Simulator sim = Client.Network.CurrentSim;
                    terrainImage = TerrainSplat.Splat(instance, heightTable,
                        new UUID[] { sim.TerrainDetail0, sim.TerrainDetail1, sim.TerrainDetail2, sim.TerrainDetail3 },
                        new float[] { sim.TerrainStartHeight00, sim.TerrainStartHeight01, sim.TerrainStartHeight10, sim.TerrainStartHeight11 },
                        new float[] { sim.TerrainHeightRange00, sim.TerrainHeightRange01, sim.TerrainHeightRange10, sim.TerrainHeightRange11 },
                        Vector3.Zero);

                    fetchingTerrainTexture = false;
                });
            }
        }

        private void RenderTerrain()
        {
            GL.Color3(1f, 1f, 1f);
            GL.EnableClientState(ArrayCap.VertexArray);
            GL.EnableClientState(ArrayCap.TextureCoordArray);
            GL.EnableClientState(ArrayCap.NormalArray);

            if (TerrainModified)
            {
                ResetTerrain(false);
                UpdateTerrain();
                UpdateTerrainTexture();
            }

            if (terrainImage != null)
            {
                if (terrainTexture != -1)
                {
                    GL.DeleteTexture(terrainTexture);
                }

                terrainTexture = GLLoadImage(terrainImage, false);
                terrainImage.Dispose();
                terrainImage = null;
            }

            if (terrainTexture == -1)
            {
                return;
            }
            else
            {
                GL.Enable(EnableCap.Texture2D);
                GL.BindTexture(TextureTarget.Texture2D, terrainTexture);
            }

            if (!useVBO)
            {
                unsafe
                {
                    fixed (float* normalPtr = &terrainVertices[0].Normal.X)
                    fixed (float* texPtr = &terrainVertices[0].TexCoord.X)
                    {
                        GL.NormalPointer(NormalPointerType.Float, FaceData.VertexSize, (IntPtr)normalPtr);
                        GL.TexCoordPointer(2, TexCoordPointerType.Float, FaceData.VertexSize, (IntPtr)texPtr);
                        GL.VertexPointer(3, VertexPointerType.Float, FaceData.VertexSize, terrainVertices);
                        GL.DrawElements(BeginMode.Triangles, terrainIndices.Length, DrawElementsType.UnsignedShort, terrainIndices);
                    }
                }
            }
            else
            {
                if (terrainVBO == -1)
                {
                    GL.GenBuffers(1, out terrainVBO);
                    GL.BindBuffer(BufferTarget.ArrayBuffer, terrainVBO);
                    GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(terrainVertices.Length * FaceData.VertexSize), terrainVertices, BufferUsageHint.StaticDraw);
                }
                else
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, terrainVBO);
                }

                if (terrainIndexVBO == -1)
                {
                    GL.GenBuffers(1, out terrainIndexVBO);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, terrainIndexVBO);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(terrainIndices.Length * sizeof(ushort)), terrainIndices, BufferUsageHint.StaticDraw);
                }
                else
                {
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, terrainIndexVBO);
                }

                GL.NormalPointer(NormalPointerType.Float, FaceData.VertexSize, (IntPtr)12);
                GL.TexCoordPointer(2, TexCoordPointerType.Float, FaceData.VertexSize, (IntPtr)(24));
                GL.VertexPointer(3, VertexPointerType.Float, FaceData.VertexSize, (IntPtr)(0));

                GL.DrawElements(BeginMode.Triangles, terrainIndices.Length, DrawElementsType.UnsignedShort, IntPtr.Zero);

                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            }
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.DisableClientState(ArrayCap.VertexArray);
            GL.DisableClientState(ArrayCap.TextureCoordArray);
            GL.DisableClientState(ArrayCap.NormalArray);
        }
        #endregion Terrain

        private void ResetMaterial()
        {
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Ambient, new float[] { 0.2f, 0.2f, 0.2f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Diffuse, new float[] { 0.8f, 0.8f, 0.8f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Specular, new float[] { 0f, 0f, 0f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Emission, new float[] { 0f, 0f, 0f, 1.0f });
            GL.Material(MaterialFace.FrontAndBack, MaterialParameter.Shininess, 0f);
        }

        float LODFactor(Vector3 pos, Vector3 primScale, float radius)
        {
            float distance = Vector3.Distance(Camera.Position, pos);
            float scale = primScale.X;
            if (primScale.Y > scale) scale = primScale.Y;
            if (primScale.Z > scale) scale = primScale.Z;
            return scale * radius * radius / distance;
        }

        private void RenderObjects(RenderPass pass)
        {
            lock (Prims)
            {
                GL.EnableClientState(ArrayCap.VertexArray);
                GL.EnableClientState(ArrayCap.TextureCoordArray);
                GL.EnableClientState(ArrayCap.NormalArray);

                int primNr = 0;
                foreach (FacetedMesh mesh in Prims.Values)
                {
                    primNr++;
                    Primitive prim = mesh.Prim;
                    FacetedMesh parent = null;
                    RenderAvatar parentav = null;

                    if (prim.ParentID != 0 && !Prims.TryGetValue(prim.ParentID, out parent) && !Avatars.TryGetValue(prim.ParentID, out parentav)) continue;
                    Vector3 primPos = PrimPos(prim);

                    // Individual prim matrix
                    GL.PushMatrix();

                    //TODO we really need a scene graph but for the moment simple if/else logic checks and parent finding are used
                    if (prim.ParentID != 0) // Does this prim have a parent
                    {
                        if (parent != null) // Check the parent is valid
                        {
                            if (parent.Prim.ParentID != 0) // Does the parent have a parent? if so it must be an avatar 
                            {
                                if (Avatars.TryGetValue(parent.Prim.ParentID, out parentav)) // Get the parent avatar
                                {
                                    // Child prims of parents that have avatar as parent

                                    if (prim.PrimData.AttachmentPoint >= AttachmentPoint.HUDCenter2 && prim.PrimData.AttachmentPoint <= AttachmentPoint.HUDBottomRight)
                                    {
                                        // Child HUD elements
                                    }
                                    else
                                    {
                                        //This is a child prim attachment

                                        // Apply prim translation and rotation relative to the root prim
                                        GL.MultMatrix(Math3D.CreateTranslationMatrix(parentav.avatar.Position));
                                        GL.MultMatrix(Math3D.CreateRotationMatrix(parentav.avatar.Rotation));

                                        int attachment_index = (int)parent.Prim.PrimData.AttachmentPoint;
                                        if (attachment_index > GLAvatar.attachment_points.Count())
                                        {
                                            // invalid LL attachment point
                                            continue;
                                        }

                                        attachment_point apoint = GLAvatar.attachment_points[attachment_index];

                                        Vector3 point = parentav.glavatar.skel.getOffset(apoint.joint) + apoint.position;
                                        Quaternion qrot = parentav.glavatar.skel.getRotation(apoint.joint) * apoint.rotation;
                                        //Vector3 point = Bone.getOffset(apoint.joint) + apoint.position;
                                       // Quaternion qrot = Bone.getRotation(apoint.joint) * apoint.rotation;

                                        GL.MultMatrix(Math3D.CreateTranslationMatrix(point));
                                        GL.MultMatrix(Math3D.CreateRotationMatrix(qrot));

                                        // Apply prim translation and rotation relative to the root prim
                                        GL.MultMatrix(Math3D.CreateTranslationMatrix(parent.Prim.Position));
                                        GL.MultMatrix(Math3D.CreateRotationMatrix(parent.Prim.Rotation));
                                    }
                                }
                            }
                            else // This prims parent does not have a parent there for its a regular prim and this must be its child
                            {
                                // Regular child prim
                                // Apply prim translation and rotation relative to the root prim
                                GL.MultMatrix(Math3D.CreateTranslationMatrix(parent.Prim.Position));
                                GL.MultMatrix(Math3D.CreateRotationMatrix(parent.Prim.Rotation));
                            }
                        }
                        else // This prim has a parent but does not have a parent in the ObjectsList, its parent is therefor parentav from the avatars list 
                        {
                            //Root prims with avatar as parent

                            if (prim.PrimData.AttachmentPoint >= AttachmentPoint.HUDCenter2 && prim.PrimData.AttachmentPoint <= AttachmentPoint.HUDBottomRight)
                            {
                                // Root HUD elements
                            }
                            else
                            {
                                // Root attachments

                                // Apply prim translation and rotation relative to the root prim
                                GL.MultMatrix(Math3D.CreateTranslationMatrix(parentav.avatar.Position));
                                GL.MultMatrix(Math3D.CreateRotationMatrix(parentav.avatar.Rotation));

                                int attachment_index = (int)prim.PrimData.AttachmentPoint;
                                if (attachment_index > GLAvatar.attachment_points.Count())
                                {
                                    // invalid LL attachment point
                                    continue;
                                }

                                attachment_point apoint = GLAvatar.attachment_points[attachment_index];

                                //Vector3 point = Bone.getOffset(apoint.joint) + apoint.position;
                                //Quaternion qrot = Bone.getRotation(apoint.joint) * apoint.rotation;
                                Vector3 point = parentav.glavatar.skel.getOffset(apoint.joint) + apoint.position;
                                Quaternion qrot = parentav.glavatar.skel.getRotation(apoint.joint) * apoint.rotation;
                                
                                GL.MultMatrix(Math3D.CreateTranslationMatrix(point));
                                GL.MultMatrix(Math3D.CreateRotationMatrix(qrot));
                            }
                        }
                    }


                    // Prim roation and position
                    GL.MultMatrix(Math3D.CreateTranslationMatrix(prim.Position));
                    GL.MultMatrix(Math3D.CreateRotationMatrix(prim.Rotation));

                    // Prim scaling
                    GL.Scale(prim.Scale.X, prim.Scale.Y, prim.Scale.Z);

                    // Do we have animated texture on this face
                    bool animatedTexture = false;

                    // Draw the prim faces
                    for (int j = 0; j < mesh.Faces.Count; j++)
                    {
                        Primitive.TextureEntryFace teFace = mesh.Prim.Textures.FaceTextures[j];
                        Face face = mesh.Faces[j];
                        FaceData data = (FaceData)face.UserData;

                        // Don't render objects too small to matter
                        if (LODFactor(primPos, prim.Scale, data.BoundingSphere.R) < minLODFactor) continue;

                        // Don't render objects not in the field of view
                        if (!Frustum.ObjectInFrustum(primPos, data.BoundingSphere, prim.Scale)) continue;

                        if (teFace == null)
                            teFace = mesh.Prim.Textures.DefaultTexture;

                        if (teFace == null)
                            continue;

                        int lightsEnabled;
                        GL.GetInteger(GetPName.Lighting, out lightsEnabled);

                        if (pass != RenderPass.Picking)
                        {
                            bool belongToAlphaPass = (teFace.RGBA.A < 0.99) || data.TextureInfo.HasAlpha;

                            if (belongToAlphaPass && pass != RenderPass.Alpha) continue;
                            if (!belongToAlphaPass && pass == RenderPass.Alpha) continue;

                            // Don't render transparent faces
                            if (teFace.RGBA.A <= 0.01f) continue;

                            if (teFace.Fullbright && lightsEnabled != 0)
                            {
                                GL.Disable(EnableCap.Lighting);
                            }

                            switch (teFace.Shiny)
                            {
                                case Shininess.High:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 0.94f);
                                    break;

                                case Shininess.Medium:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 0.64f);
                                    break;

                                case Shininess.Low:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 0.24f);
                                    break;


                                case Shininess.None:
                                default:
                                    GL.Material(MaterialFace.Front, MaterialParameter.Shininess, 0f);
                                    break;
                            }

                            var faceColor = new float[] { teFace.RGBA.R, teFace.RGBA.G, teFace.RGBA.B, teFace.RGBA.A };
                            GL.Color4(faceColor);

                            GL.Material(MaterialFace.Front, MaterialParameter.Specular, new float[] { 0.5f, 0.5f, 0.5f, 1f });

                            if (data.TextureInfo.TexturePointer != 0)
                            {
                                // Is this face using texture animation
                                if ((prim.TextureAnim.Flags & Primitive.TextureAnimMode.ANIM_ON) != 0
                                    && (prim.TextureAnim.Face == j || prim.TextureAnim.Face == 255))
                                {
                                    if (data.AnimInfo == null)
                                    {
                                        data.AnimInfo = new TextureAnimationInfo();
                                    }
                                    data.AnimInfo.PrimAnimInfo = prim.TextureAnim;
                                    data.AnimInfo.Step(lastFrameTime);
                                    animatedTexture = true;
                                }
                                else if (data.AnimInfo != null) // Face texture not animated. Do we have previous anim setting?
                                {
                                    data.AnimInfo = null;
                                }

                                GL.Enable(EnableCap.Texture2D);
                                GL.BindTexture(TextureTarget.Texture2D, data.TextureInfo.TexturePointer);
                            }
                            else
                            {
                                GL.Disable(EnableCap.Texture2D);
                            }

                        }
                        else
                        {
                            data.PickingID = primNr;
                            var primNrBytes = Utils.Int16ToBytes((short)primNr);
                            var faceColor = new byte[] { primNrBytes[0], primNrBytes[1], (byte)j, 255 };
                            GL.Color4(faceColor);
                        }

                        if (!useVBO)
                        {
                            Vertex[] verts = face.Vertices.ToArray();

                            unsafe
                            {
                                fixed (float* normalPtr = &verts[0].Normal.X)
                                fixed (float* texPtr = &verts[0].TexCoord.X)
                                {
                                    GL.NormalPointer(NormalPointerType.Float, FaceData.VertexSize, (IntPtr)normalPtr);
                                    GL.TexCoordPointer(2, TexCoordPointerType.Float, FaceData.VertexSize, (IntPtr)texPtr);
                                    GL.VertexPointer(3, VertexPointerType.Float, FaceData.VertexSize, verts);
                                    GL.DrawElements(BeginMode.Triangles, data.Indices.Length, DrawElementsType.UnsignedShort, data.Indices);
                                }
                            }
                        }
                        else
                        {
                            data.CheckVBO(face);
                            GL.BindBuffer(BufferTarget.ArrayBuffer, data.VertexVBO);
                            GL.BindBuffer(BufferTarget.ElementArrayBuffer, data.IndexVBO);
                            GL.NormalPointer(NormalPointerType.Float, FaceData.VertexSize, (IntPtr)12);
                            GL.TexCoordPointer(2, TexCoordPointerType.Float, FaceData.VertexSize, (IntPtr)(24));
                            GL.VertexPointer(3, VertexPointerType.Float, FaceData.VertexSize, (IntPtr)(0));

                            GL.DrawElements(BeginMode.Triangles, face.Indices.Count, DrawElementsType.UnsignedShort, IntPtr.Zero);

                            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

                        }

                        if (teFace.Fullbright && lightsEnabled != 0)
                        {
                            GL.Enable(EnableCap.Lighting);
                        }
                    }

                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    ResetMaterial();

                    // Reset texture coordinates if we modified them in texture animation
                    if (animatedTexture)
                    {
                        GL.MatrixMode(MatrixMode.Texture);
                        GL.LoadIdentity();
                        GL.MatrixMode(MatrixMode.Modelview);
                    }

                    // Pop the prim matrix
                    GL.PopMatrix();
                }
                GL.Disable(EnableCap.Texture2D);
                GL.DisableClientState(ArrayCap.VertexArray);
                GL.DisableClientState(ArrayCap.TextureCoordArray);
                GL.DisableClientState(ArrayCap.NormalArray);
            }
        }

        void DrawWaterQuad(float x, float y, float z)
        {
            GL.Vertex3(x, y, z);
            GL.Vertex3(x + 256f, y, z);
            GL.Vertex3(x + 256f, y + 256f, z);
            GL.Vertex3(x, y + 256f, z);
        }

        public void RenderWater()
        {
            float z = Client.Network.CurrentSim.WaterHeight;

            GL.Color4(0.09f, 0.28f, 0.63f, 0.84f);

            GL.Begin(BeginMode.Quads);
            for (float x = -256f * 2; x <= 256 * 2; x += 256f)
                for (float y = -256f * 2; y <= 256 * 2; y += 256f)
                    DrawWaterQuad(x, y, z);
            GL.End();
        }

        private void Render(bool picking)
        {
            if (picking)
            {
                GL.ClearColor(1f, 1f, 1f, 1f);
            }
            else
            {
                GL.ClearColor(0.39f, 0.58f, 0.93f, 1.0f);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.LoadIdentity();

            // Setup wireframe or solid fill drawing mode
            if (Wireframe && !picking)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }
            else
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }

            var mLookAt = OpenTK.Matrix4d.LookAt(
                    Camera.RenderPosition.X, Camera.RenderPosition.Y, Camera.RenderPosition.Z,
                    Camera.RenderFocalPoint.X, Camera.RenderFocalPoint.Y, Camera.RenderFocalPoint.Z,
                    0d, 0d, 1d);
            GL.MultMatrix(ref mLookAt);

            GL.Light(LightName.Light0, LightParameter.Position, lightPos);

            // Push the world matrix
            GL.PushMatrix();

            if (Camera.Modified)
            {
                GL.GetFloat(GetPName.ProjectionMatrix, out ProjectionMatrix);
                GL.GetFloat(GetPName.ModelviewMatrix, out ModelMatrix);
                GL.GetInteger(GetPName.Viewport, Viewport);
                Frustum.CalculateFrustum(ProjectionMatrix, ModelMatrix);
                UpdateCamera();
                Camera.Modified = false;
            }

            if (Camera.TimeToTarget != 0)
            {
                Camera.Step(lastFrameTime);
            }

            if (picking)
            {
                RenderObjects(RenderPass.Picking);
                RenderAvatars(RenderPass.Picking);
            }
            else
            {
                RenderTerrain();
                RenderObjects(RenderPass.Simple);
                RenderAvatarsSkeleton(RenderPass.Simple);
                RenderAvatars(RenderPass.Simple);

                GL.Disable(EnableCap.Lighting);
                RenderWater();
                RenderObjects(RenderPass.Alpha);
                GL.Enable(EnableCap.Lighting);

                GLHUDBegin();
                RenderText();
                RenderStats();
                GLHUDEnd();
            }

            // Pop the world matrix
            GL.PopMatrix();
        }

        private void GluPerspective(float fovy, float aspect, float zNear, float zFar)
        {
            float fH = (float)Math.Tan(fovy / 360 * (float)Math.PI) * zNear;
            float fW = fH * aspect;
            GL.Frustum(-fW, fW, -fH, fH, zNear, zFar);
        }

        private bool TryPick(int x, int y, out object picked, out int faceID)
        {
            // Save old attributes
            GL.PushAttrib(AttribMask.AllAttribBits);

            // Disable some attributes to make the objects flat / solid color when they are drawn
            GL.Disable(EnableCap.Fog);
            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Dither);
            GL.Disable(EnableCap.Lighting);
            GL.Disable(EnableCap.LineStipple);
            GL.Disable(EnableCap.PolygonStipple);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.AlphaTest);

            Render(true);

            byte[] color = new byte[4];
            GL.ReadPixels(x, glControl.Height - y, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, color);

            GL.PopAttrib();

            int primID = Utils.BytesToUInt16(color, 0);
            faceID = color[2];

            picked = null;

            if (color[3] == 254) // Avatar
            {
                lock (Avatars)
                {
                    foreach (var avatar in Avatars.Values)
                    {
                        for (int i = 0; i < avatar.data.Length; i++)
                        {
                            var face = avatar.data[i];
                            if (face != null && face.PickingID == primID)
                            {
                                picked = avatar;
                                break;
                            }
                        }
                    }
                }

                if (picked != null)
                {
                    return true;
                }
            }

            if (color[3] == 255) // Prim
            {
                lock (Prims)
                {
                    foreach (var mesh in Prims.Values)
                    {
                        foreach (var face in mesh.Faces)
                        {
                            if (face.UserData == null) continue;
                            if (((FaceData)face.UserData).PickingID == primID)
                            {
                                picked = mesh;
                                break;
                            }
                        }

                        if (picked != null) break;
                    }
                }
            }

            return picked != null;
        }

        public void DownloadTexture(TextureLoadItem item)
        {
            lock (TexturesPtrMap)
            {
                if (TexturesPtrMap.ContainsKey(item.TeFace.TextureID))
                {
                    item.Data.TextureInfo = TexturesPtrMap[item.TeFace.TextureID];
                }
                else
                {
                    TexturesPtrMap[item.TeFace.TextureID] = item.Data.TextureInfo;

                    if (item.TextureData == null)
                    {
                        Client.Assets.RequestImage(item.TeFace.TextureID, (state, asset) =>
                        {
                            if (state == TextureRequestState.Finished)
                            {
                                item.TextureData = asset.AssetData;
                                PendingTextures.Enqueue(item);
                            }
                        });
                    }
                    else
                    {
                        PendingTextures.Enqueue(item);
                    }
                }
            }
        }

        private void MeshPrim(Primitive prim, FacetedMesh mesh)
        {
            FacetedMesh existingMesh = null;

            lock (Prims)
            {
                if (Prims.ContainsKey(prim.LocalID))
                {
                    existingMesh = Prims[prim.LocalID];
                }
            }

            // Create a FaceData struct for each face that stores the 3D data
            // in a OpenGL friendly format
            for (int j = 0; j < mesh.Faces.Count; j++)
            {
                Primitive.TextureEntryFace teFace = prim.Textures.GetFace((uint)j);
                if (teFace == null) continue;

                Face face = mesh.Faces[j];
                FaceData data = new FaceData();

                // Vertices for this face
                data.Vertices = new float[face.Vertices.Count * 3];
                data.Normals = new float[face.Vertices.Count * 3];
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.Vertices[k * 3 + 0] = face.Vertices[k].Position.X;
                    data.Vertices[k * 3 + 1] = face.Vertices[k].Position.Y;
                    data.Vertices[k * 3 + 2] = face.Vertices[k].Position.Z;

                    if (data.Vertices[k * 3 + 0] < data.BoundingSphere.Min.X) data.BoundingSphere.Min.X = data.Vertices[k * 3 + 0];
                    if (data.Vertices[k * 3 + 1] < data.BoundingSphere.Min.Y) data.BoundingSphere.Min.Y = data.Vertices[k * 3 + 1];
                    if (data.Vertices[k * 3 + 2] < data.BoundingSphere.Min.Z) data.BoundingSphere.Min.Z = data.Vertices[k * 3 + 2];

                    if (data.Vertices[k * 3 + 0] > data.BoundingSphere.Max.X) data.BoundingSphere.Max.X = data.Vertices[k * 3 + 0];
                    if (data.Vertices[k * 3 + 1] > data.BoundingSphere.Max.Y) data.BoundingSphere.Max.Y = data.Vertices[k * 3 + 1];
                    if (data.Vertices[k * 3 + 2] > data.BoundingSphere.Max.Z) data.BoundingSphere.Max.Z = data.Vertices[k * 3 + 2];

                    data.Normals[k * 3 + 0] = face.Vertices[k].Normal.X;
                    data.Normals[k * 3 + 1] = face.Vertices[k].Normal.Y;
                    data.Normals[k * 3 + 2] = face.Vertices[k].Normal.Z;
                }

                data.BoundingSphere.R = (data.BoundingSphere.Max - data.BoundingSphere.Min).Length();

                // Indices for this face
                data.Indices = face.Indices.ToArray();

                // With linear texture animation in effect, texture repeats and offset are ignored
                if ((prim.TextureAnim.Flags & Primitive.TextureAnimMode.ANIM_ON) != 0
                    && (prim.TextureAnim.Flags & Primitive.TextureAnimMode.ROTATE) == 0
                    && (prim.TextureAnim.Face == 255 || prim.TextureAnim.Face == j))
                {
                    teFace.RepeatU = 1;
                    teFace.RepeatV = 1;
                    teFace.OffsetU = 0;
                    teFace.OffsetV = 0;
                }

                // Need to adjust UV for spheres as they are sort of half-prim
                if (prim.PrimData.ProfileCurve == ProfileCurve.HalfCircle)
                {
                    teFace = new Primitive.TextureEntryFace(teFace);
                    teFace.RepeatV *= 2;
                    teFace.OffsetV += 0.5f;
                }

                // Texture transform for this face
                renderer.TransformTexCoords(face.Vertices, face.Center, teFace);

                // Texcoords for this face
                data.TexCoords = new float[face.Vertices.Count * 2];
                for (int k = 0; k < face.Vertices.Count; k++)
                {
                    data.TexCoords[k * 2 + 0] = face.Vertices[k].TexCoord.X;
                    data.TexCoords[k * 2 + 1] = face.Vertices[k].TexCoord.Y;
                }

                // Set the UserData for this face to our FaceData struct
                face.UserData = data;
                mesh.Faces[j] = face;


                if (existingMesh != null &&
                    j < existingMesh.Faces.Count &&
                    existingMesh.Faces[j].TextureFace.TextureID == teFace.TextureID &&
                    ((FaceData)existingMesh.Faces[j].UserData).TextureInfo.TexturePointer != 0
                    )
                {
                    FaceData existingData = (FaceData)existingMesh.Faces[j].UserData;
                    data.TextureInfo.TexturePointer = existingData.TextureInfo.TexturePointer;
                }
                else
                {

                    DownloadTexture(new TextureLoadItem()
                    {
                        Data = data,
                        Prim = prim,
                        TeFace = teFace
                    });
                }
            }

            lock (Prims)
            {
                Prims[prim.LocalID] = mesh;
            }
        }

        private void UpdatePrimBlocking(Primitive prim)
        {
            if (Vector3.Distance(PrimPos(prim), Client.Self.SimPosition) > DrawDistance && !Prims.ContainsKey(prim.ParentID) && !Avatars.ContainsKey(prim.ParentID)) return;

            if (Client.Network.CurrentSim.ObjectsAvatars.ContainsKey(prim.LocalID))
            {
                AddAvatarToScene(Client.Network.CurrentSim.ObjectsAvatars[prim.LocalID]);
                return;
            }

            // Skip foliage
            if (prim.PrimData.PCode != PCode.Prim) return;

            if (prim.Textures == null) return;

            // Regular prim
            if (prim.Sculpt == null || prim.Sculpt.SculptTexture == UUID.Zero)
            {
                MeshPrim(prim, renderer.GenerateFacetedMesh(prim, DetailLevel.High));
            }
            else
            {
                try
                {
                    FacetedMesh mesh = null;

                    if (prim.Sculpt.Type != SculptType.Mesh)
                    { // Regular sculptie
                        Image img = null;

                        lock (sculptCache)
                        {
                            if (sculptCache.ContainsKey(prim.Sculpt.SculptTexture))
                            {
                                img = sculptCache[prim.Sculpt.SculptTexture];
                            }
                        }

                        if (img == null)
                        {
                            if (LoadTexture(prim.Sculpt.SculptTexture, ref img, true))
                            {
                                sculptCache[prim.Sculpt.SculptTexture] = (Bitmap)img;
                            }
                            else
                            {
                                return;
                            }
                        }

                        mesh = renderer.GenerateFacetedSculptMesh(prim, (Bitmap)img, DetailLevel.High);
                    }
                    else
                    { // Mesh
                        AutoResetEvent gotMesh = new AutoResetEvent(false);
                        bool meshSuccess = false;

                        Client.Assets.RequestMesh(prim.Sculpt.SculptTexture, (success, meshAsset) =>
                            {
                                if (!success || !FacetedMesh.TryDecodeFromAsset(prim, meshAsset, DetailLevel.Highest, out mesh))
                                {
                                    Logger.Log("Failed to fetch or decode the mesh asset", Helpers.LogLevel.Warning, Client);
                                }
                                else
                                {
                                    meshSuccess = true;
                                }
                                gotMesh.Set();
                            });

                        if (!gotMesh.WaitOne(20 * 1000, false)) return;
                        if (!meshSuccess) return;
                    }

                    if (mesh != null)
                    {
                        MeshPrim(prim, mesh);
                    }
                }
                catch
                { }
            }
        }

        private bool LoadTexture(UUID textureID, ref Image texture, bool removeAlpha)
        {
            ManualResetEvent gotImage = new ManualResetEvent(false);
            Image img = null;

            try
            {
                gotImage.Reset();
                instance.Client.Assets.RequestImage(textureID, (TextureRequestState state, AssetTexture assetTexture) =>
                    {
                        if (state == TextureRequestState.Finished)
                        {
                            ManagedImage mi;
                            OpenJPEG.DecodeToImage(assetTexture.AssetData, out mi);

                            if (removeAlpha)
                            {
                                if ((mi.Channels & ManagedImage.ImageChannels.Alpha) != 0)
                                {
                                    mi.ConvertChannels(mi.Channels & ~ManagedImage.ImageChannels.Alpha);
                                }
                            }

                            img = LoadTGAClass.LoadTGA(new MemoryStream(mi.ExportTGA()));
                        }
                        gotImage.Set();
                    }
                );
                gotImage.WaitOne(30 * 1000, false);
                if (img != null)
                {
                    texture = img;
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Logger.Log(e.Message, Helpers.LogLevel.Error, instance.Client, e);
                return false;
            }
        }
        #endregion Private methods (the meat)

        #region Form controls handlers
        private void chkWireFrame_CheckedChanged(object sender, EventArgs e)
        {
            Wireframe = chkWireFrame.Checked;
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            InitCamera();
        }

        private void cbAA_CheckedChanged(object sender, EventArgs e)
        {
            instance.GlobalSettings["use_multi_sampling"] = UseMultiSampling = cbAA.Checked;
            SetupGLControl();
        }

        #endregion Form controls handlers

        #region Context menu
        private void ctxObjects_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (instance.State.IsSitting)
            {
                sitToolStripMenuItem.Text = "Stand up";
            }
            else if (RightclickedPrim.Prim.Properties != null
                && !string.IsNullOrEmpty(RightclickedPrim.Prim.Properties.SitName))
            {
                sitToolStripMenuItem.Text = RightclickedPrim.Prim.Properties.SitName;
            }
            else
            {
                sitToolStripMenuItem.Text = "Sit";
            }

            if (RightclickedPrim.Prim.Properties != null
                && !string.IsNullOrEmpty(RightclickedPrim.Prim.Properties.TouchName))
            {
                touchToolStripMenuItem.Text = RightclickedPrim.Prim.Properties.TouchName;
            }
            else
            {
                touchToolStripMenuItem.Text = "Touch";
            }
        }

        private void touchToolStripMenuItem_Click(object sender, EventArgs e)
        {

            Client.Self.Grab(RightclickedPrim.Prim.LocalID, Vector3.Zero, Vector3.Zero, Vector3.Zero, RightclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
            Thread.Sleep(100);
            Client.Self.DeGrab(RightclickedPrim.Prim.LocalID);
        }

        private void sitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!instance.State.IsSitting)
            {
                instance.State.SetSitting(true, RightclickedPrim.Prim.ID);
            }
            else
            {
                instance.State.SetSitting(false, UUID.Zero);
            }
        }

        private void takeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
            Client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID);
            Close();
        }

        private void returnToolStripMenuItem_Click(object sender, EventArgs e)
        {
            instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
            Client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID, DeRezDestination.ReturnToOwner, UUID.Zero, UUID.Random());
            Close();
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (RightclickedPrim.Prim.Properties != null && RightclickedPrim.Prim.Properties.OwnerID != Client.Self.AgentID)
                returnToolStripMenuItem_Click(sender, e);
            else
            {
                instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
                Client.Inventory.RequestDeRezToInventory(RightclickedPrim.Prim.LocalID, DeRezDestination.AgentInventoryTake, Client.Inventory.FindFolderForType(AssetType.TrashFolder), UUID.Random());
            }
            Close();
        }
        #endregion Context menu

        private void hsAmbient_Scroll(object sender, ScrollEventArgs e)
        {
            ambient = (float)hsAmbient.Value / 100f;
            SetSun();
        }

        private void hsDiffuse_Scroll(object sender, ScrollEventArgs e)
        {
            difuse = (float)hsDiffuse.Value / 100f;
            SetSun();
        }

        private void hsSpecular_Scroll(object sender, ScrollEventArgs e)
        {
            specular = (float)hsSpecular.Value / 100f;
            SetSun();
        }

        private void hsLOD_Scroll(object sender, ScrollEventArgs e)
        {
            minLODFactor = (float)hsLOD.Value / 5000f;
        }

        private void button_vparam_Click(object sender, EventArgs e)
        {
            //int paramid = int.Parse(textBox_vparamid.Text);
            //float weight = (float)hScrollBar_weight.Value/100f;
            float weightx = float.Parse(textBox_x.Text);
            float weighty = float.Parse(textBox_y.Text);
            float weightz = float.Parse(textBox_z.Text);

            foreach (RenderAvatar av in Avatars.Values)
            {
                //av.glavatar.morphtest(av.avatar,paramid,weight);
                av.glavatar.skel.deformbone(comboBox1.Text, new Vector3(0, 0, 0), new Vector3(float.Parse(textBox_sx.Text), float.Parse(textBox_sy.Text), float.Parse(textBox_sz.Text)), Quaternion.CreateFromEulers((float)(Math.PI * (weightx / 180)), (float)(Math.PI * (weighty / 180)), (float)(Math.PI * (weightz / 180))));

                foreach (GLMesh mesh in av.glavatar._meshes.Values)
                {
                    mesh.applyjointweights();
                }

           }
        }

        private void textBox_vparamid_TextChanged(object sender, EventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

            string bone = comboBox1.Text;
            foreach (RenderAvatar av in Avatars.Values)
            {
                Bone b;
                if (av.glavatar.skel.mBones.TryGetValue(bone, out b))
                {
                    textBox_sx.Text = (b.scale.X-1.0f).ToString();
                    textBox_sy.Text = (b.scale.Y-1.0f).ToString();
                    textBox_sz.Text = (b.scale.Z-1.0f).ToString();

                    float x,y,z;
                    b.rot.GetEulerAngles(out x, out y, out z);
                    textBox_x.Text = x.ToString();
                    textBox_y.Text = y.ToString();
                    textBox_z.Text = z.ToString();

                }

            }
            

        }

        private void textBox_y_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox_z_TextChanged(object sender, EventArgs e)
        {

        }

        private void comboBox_morph_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            foreach (RenderAvatar av in Avatars.Values)
            {
                int id = -1;
                foreach (VisualParamEx vpe in VisualParamEx.morphParams.Values)
                {
                    if (vpe.Name == comboBox_morph.Text)
                    {
                        id = vpe.ParamID;
                        break;
                    }

                }
                av.glavatar.morphtest(av.avatar, id, float.Parse(textBox_morphamount.Text));

                foreach (GLMesh mesh in av.glavatar._meshes.Values)
                {
                    mesh.applyjointweights();
                }

            }

                    

        }

        private void gbZoom_Enter(object sender, EventArgs e)
        {

        }

        private void button_driver_Click(object sender, EventArgs e)
        {
            foreach (RenderAvatar av in Avatars.Values)
            {
                int id = -1;
                foreach (VisualParamEx vpe in VisualParamEx.drivenParams.Values)
                {
                    if (vpe.Name == comboBox_driver.Text)
                    {
                        id = vpe.ParamID;
                        break;
                    }

                }
                av.glavatar.morphtest(av.avatar, id, float.Parse(textBox_driveramount.Text));

                foreach (GLMesh mesh in av.glavatar._meshes.Values)
                {
                    mesh.applyjointweights();
                }

            }

        }

       

    }
}