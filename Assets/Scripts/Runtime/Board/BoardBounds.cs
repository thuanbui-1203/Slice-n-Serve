using SliceNServe.Data;
using UnityEngine;

namespace SliceNServe.Board
{
    /// <summary>
    /// Builds the four board walls from <see cref="BoardConfig.worldBounds"/> so the collider
    /// inner faces sit exactly on the boundary, plus the floor and backdrop behind them.
    ///
    /// The 2.0 u collider thickness is load-bearing, not decoration: at the project's 0.02 s
    /// fixed step the knife travels 0.8 u per tick at the 40 u/s cap (plan §4.6), so anything
    /// thinner than that can be tunnelled through in a single step.
    /// </summary>
    public sealed class BoardBounds : MonoBehaviour
    {
        [SerializeField] private Sprite wallSprite;
        [SerializeField] private Sprite floorSprite;
        [SerializeField] private PhysicsMaterial2D wallMaterial;
        [SerializeField] private float colliderThickness = 2f;

        [Tooltip("How far the visible border reaches into the play area. At 16:9 the board fills "
               + "the screen exactly, so a border drawn only outside it would never be seen.")]
        [SerializeField] private float visualInset = 0.14f;

        [SerializeField] private float floorTileSize = 1.2f;
        [SerializeField] private Color wallColor = new Color(0.36f, 0.22f, 0.15f);
        [SerializeField] private Color backdropColor = new Color(0.16f, 0.11f, 0.10f);

        public Rect PlayArea { get; private set; }
        public Vector2 LauncherPosition { get; private set; }

        private BoardConfig _config;

        public void Initialize(BoardConfig config)
        {
            _config = config;
            PlayArea = config.worldBounds;
            LauncherPosition = config.launcherPosition;
            Build();
        }

        private void Build()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            Rect b = _config.worldBounds;
            Vector2 c = b.center;
            float hw = b.width * 0.5f, hh = b.height * 0.5f;
            float t = colliderThickness;

            // Horizontal walls span the corners; vertical walls fit between them, so there are no
            // internal corners for the knife to wedge into (plan §5.1).
            AddWall("WallTop", c + new Vector2(0f, hh + t * 0.5f), new Vector2(b.width + t * 2f, t));
            AddWall("WallBottom", c + new Vector2(0f, -hh - t * 0.5f), new Vector2(b.width + t * 2f, t));
            AddWall("WallLeft", c + new Vector2(-hw - t * 0.5f, 0f), new Vector2(t, b.height));
            AddWall("WallRight", c + new Vector2(hw + t * 0.5f, 0f), new Vector2(t, b.height));

            // Visible border: a frame straddling the boundary, mostly outside it so wide screens
            // see a counter edge, and `visualInset` inside it so 16:9 sees one too.
            float v = visualInset, outer = 0.5f;
            float thick = v + outer, mid = (outer - v) * 0.5f;
            AddVisual("BorderTop", c + new Vector2(0f, hh + mid), new Vector2(b.width + thick * 2f, thick), wallSprite, wallColor, 2);
            AddVisual("BorderBottom", c + new Vector2(0f, -hh - mid), new Vector2(b.width + thick * 2f, thick), wallSprite, wallColor, 2);
            AddVisual("BorderLeft", c + new Vector2(-hw - mid, 0f), new Vector2(thick, b.height), wallSprite, wallColor, 2);
            AddVisual("BorderRight", c + new Vector2(hw + mid, 0f), new Vector2(thick, b.height), wallSprite, wallColor, 2);

            AddFloor(c, b.size);
            AddVisual("Backdrop", c, new Vector2(b.width * 4f, b.height * 4f), wallSprite, backdropColor, -20, 1f);
        }

        private void AddWall(string wallName, Vector2 center, Vector2 colliderSize)
        {
            var go = new GameObject(wallName);
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            go.layer = LayerMask.NameToLayer("BoardWall");

            var box = go.AddComponent<BoxCollider2D>();
            box.size = colliderSize;
            box.sharedMaterial = wallMaterial;
        }

        /// <summary>Visuals are separate objects from the colliders: scaling a transform that also
        /// carries a BoxCollider2D would scale the collider with it.</summary>
        private void AddVisual(string visualName, Vector2 center, Vector2 size, Sprite sprite, Color color, int order, float z = 0f)
        {
            if (sprite == null) return;

            var go = new GameObject(visualName);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(center.x, center.y, z);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;

            Vector2 native = sprite.bounds.size;
            if (native.x > 0f && native.y > 0f)
                go.transform.localScale = new Vector3(size.x / native.x, size.y / native.y, 1f);
        }

        private void AddFloor(Vector2 center, Vector2 size)
        {
            if (floorSprite == null) return;

            var go = new GameObject("Floor");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(center.x, center.y, 0.5f);

            // Tiled draw mode needs the texture imported as Full Rect with Repeat wrap; scaling
            // the transform sets the tile size, and the renderer size (in sprite units) covers the board.
            float spriteSize = floorSprite.bounds.size.x;
            float scale = floorTileSize / spriteSize;
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = floorSprite;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size / scale;
            sr.sortingOrder = -10;
        }
    }
}
