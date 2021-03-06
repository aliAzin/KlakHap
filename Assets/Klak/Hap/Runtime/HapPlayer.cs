using UnityEngine;

namespace Klak.Hap
{
    [AddComponentMenu("Klak/HAP/HAP Player")]
    public sealed class HapPlayer : MonoBehaviour
    {
        #region Editable attributes

        public enum PathMode { StreamingAssets, LocalFileSystem }

        [SerializeField] PathMode _pathMode = PathMode.StreamingAssets;
        [SerializeField] string _filePath = "";

        [SerializeField] float _time = 0;
        [SerializeField, Range(-10, 10)] float _speed = 1;
        [SerializeField] bool _loop = true;

        [SerializeField] RenderTexture _targetTexture = null;
        [SerializeField] Renderer _targetRenderer = null;
        [SerializeField] string _targetMaterialProperty = "_MainTex";

        #endregion

        #region Public properties

        public float time {
            get { return _time; }
            set { _time = value; }
        }

        public float speed {
            get { return _speed; }
            set { _speed = value; }
        }

        public bool loop {
            get { return _loop; }
            set { _loop = value; }
        }

        public RenderTexture targetTexture {
            get { return _targetTexture; }
            set { _targetTexture = value; }
        }

        public Renderer targetRenderer {
            get { return _targetRenderer; }
            set { _targetRenderer = value; }
        }

        public string targetMaterialProperty {
            get { return _targetMaterialProperty; }
            set { _targetMaterialProperty = value; }
        }

        #endregion

        #region Read-only properties

        public int frameWidth { get { return _demuxer?.Width ?? 0; } }
        public int frameHeight { get { return _demuxer?.Height ?? 0; } }
        public int frameCount { get { return _demuxer?.FrameCount ?? 0; } }
        public double streamDuration { get { return _demuxer?.Duration ?? 0; } }

        public CodecType codecType { get {
            return Utility.DetermineCodecType(_demuxer?.VideoType ?? 0);
        } }

        public string resolvedFilePath { get {
            if (_pathMode == PathMode.StreamingAssets)
                return System.IO.Path.Combine(Application.streamingAssetsPath, _filePath);
            else
                return _filePath;
        } }

        public Texture2D texture { get { return _texture; } }

        #endregion

        #region Public methods

        public void Open(string filePath, PathMode pathMode = PathMode.StreamingAssets)
        {
            if (_demuxer != null)
            {
                Debug.LogError("Stream has already been opened.");
                return;
            }

            _filePath = filePath;
            _pathMode = pathMode;

            OpenInternal();
        }

        #endregion

        #region Private members

        Demuxer _demuxer;
        StreamReader _stream;
        Decoder _decoder;

        Texture2D _texture;
        TextureUpdater _updater;

        float _storedTime;
        float _storedSpeed;

        void OpenInternal()
        {
            // Demuxer instantiation
            _demuxer = new Demuxer(resolvedFilePath);

            if (!_demuxer.IsValid)
            {
                Debug.LogError("Failed to open stream (" + resolvedFilePath + ").");
                _demuxer.Dispose();
                _demuxer = null;
                return;
            }

            // Stream reader instantiation
            _stream = new StreamReader(_demuxer, _time, _speed / 60);
            (_storedTime, _storedSpeed) = (_time, _speed);

            // Decoder instantiation
            _decoder = new Decoder(
                _stream, _demuxer.Width, _demuxer.Height, _demuxer.VideoType
            );

            // Texture initialization
            _texture = new Texture2D(
                _demuxer.Width, _demuxer.Height,
                Utility.DetermineTextureFormat(_demuxer.VideoType), false
            );
            _texture.wrapMode = TextureWrapMode.Clamp;
            _updater = new TextureUpdater(_texture, _decoder.CallbackID);
        }

        #endregion

        #region External object updaters

        Material _blitMaterial;
        MaterialPropertyBlock _propertyBlock;

        void UpdateTargetTexture()
        {
            if (_targetTexture == null) return;

            // Material lazy initialization
            if (_blitMaterial == null)
                _blitMaterial = new Material(Utility.DetermineShader(_demuxer.VideoType));

            // Blit
            Graphics.Blit(_texture, _targetTexture, _blitMaterial, 0);
        }

        void UpdateTargetRenderer()
        {
            if (_targetRenderer == null) return;

            // Material property block lazy initialization
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            // Read-modify-write
            _targetRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(_targetMaterialProperty, _texture);
            _targetRenderer.SetPropertyBlock(_propertyBlock);
        }

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            if (_demuxer == null && !string.IsNullOrEmpty(_filePath))
                OpenInternal();
        }

        void OnDestroy()
        {
            if (_updater != null)
            {
                _updater.Dispose();
                _updater = null;
            }

            if (_decoder != null)
            {
                _decoder.Dispose();
                _decoder = null;
            }

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_demuxer != null)
            {
                _demuxer.Dispose();
                _demuxer = null;
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }

            if (_blitMaterial != null)
            {
                Destroy(_blitMaterial);
                _blitMaterial = null;
            }
        }

        void LateUpdate()
        {
            if (_demuxer == null) return;

            // Restart the stream reader when the time/speed were changed.
            if (_time != _storedTime || _speed != _storedSpeed)
            {
                _stream.Restart(_time, _speed / 60);
                (_storedTime, _storedSpeed) = (_time, _speed);
            }

            // Decode and update
            _decoder.UpdateTime(_time);
            _updater.RequestUpdate();

            // Time advance
            _time += Time.deltaTime * _speed;
            if (!_loop) _time = Mathf.Clamp(_time, 0, (float)_demuxer.Duration);
            _storedTime = _time;

            // External object updates
            UpdateTargetRenderer();
            UpdateTargetTexture();
        }

        #endregion
    }
}
