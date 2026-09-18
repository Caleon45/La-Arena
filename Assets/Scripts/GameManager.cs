using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    // ==========================================
    // CONDICIONES INICIALES (Configuracion fija)
    // ==========================================
    [Header("Condiciones Iniciales")]
    public int width = 50;          // Celdas a lo ancho
    public int height = 30;         // Celdas a lo alto
    public float updateTime = 0.05f;// Tiempo T entre generaciones (segundos)

    [Header("Referencias Opcionales")]
    public GameObject cellPrefab;   // Prefab de celda (referencia del escenario)

    // ==========================================
    // VARIABLES DINAMICAS (Cambian en ejecucion)
    // ==========================================
    [Header("Variables de Ejecucion")]
    public int dia = 0;             // Contador de generacion / dia actual
    public int arenaTotal = 0;      // Conteo de particulas de arena presentes
    public bool isPaused = false;   // Estado de pausa

    private bool[,] grid;           // Estado actual (true = arena, false = vacio)
    private bool[,] nextGrid;       // Doble buffer para calcular el siguiente estado
    private float timer;            // Acumulador de tiempo para el tick T
    private Texture2D texture;      // Textura donde se dibuja la grilla
    private Color32[] pixels;       // Buffer de color (1 pixel = 1 celda)

    // Colores de la simulacion
    private static readonly Color32 SandColor = new Color32(235, 190, 80, 255); // Color arena calido
    private static readonly Color32 EmptyColor = new Color32(30, 30, 35, 255);  // Fondo vacio oscuro

    void Start()
    {
        // Inicializacion de matrices de simulacion
        grid = new bool[width, height];
        nextGrid = new bool[width, height];

        // Suscripcion a los eventos del InputManager si esta disponible
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnPause += TogglePause;
            InputManager.Instance.OnRestart += RestartSimulation;
            InputManager.Instance.OnClear += ClearSimulation;
            InputManager.Instance.OnToggleCell += ToggleCellInput;
        }

        CenterCamera();
        BuildTexture();
        RandomizeGrid();
    }

    // Centra la camara en la grilla y ajusta el zoom segun las dimensiones
    void CenterCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(width / 2f, height / 2f, -10f);
            cam.orthographicSize = (height / 2f) + 2f;
        }
    }

    void Update()
    {
        // Permite pintar arena de forma continua manteniendo presionado el clic izquierdo
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            HandleMouseClick(paintSand: true);
        }

        if (isPaused) return;

        // Ciclo de tiempo T para avanzar generaciones
        timer += Time.deltaTime;
        if (timer >= updateTime)
        {
            Step();          // 1) Calcular fisica de la arena
            UpdateVisuals(); // 2) Actualizar la imagen en pantalla
            timer = 0f;
        }
    }

    // Tecla P: Pausa o reanuda la simulacion
    void TogglePause()
    {
        isPaused = !isPaused;
        Debug.Log(isPaused ? "Simulacion pausada" : "Simulacion reanudada");
    }

    // Clic simple: alterna arena en la posicion del puntero
    void ToggleCellInput()
    {
        if (Mouse.current != null)
        {
            HandleMouseClick(paintSand: false);
            return;
        }

        Vector3 camPos = Camera.main.transform.position;
        ToggleCellAtWorld(camPos, false);
    }

    // Tecla E: Limpia todo el tablero
    void ClearSimulation()
    {
        Debug.Log("Limpiando simulacion...");
        ClearGrid();
        timer = 0f;
    }

    // Tecla R: Reinicia con una nueva distribucion de arena
    void RestartSimulation()
    {
        Debug.Log("Reiniciando simulacion...");
        RandomizeGrid();
        timer = 0f;
    }

    // Construye la textura y el SpriteRenderer
    void BuildTexture()
    {
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point; // Pixeles nitidos
        texture.wrapMode = TextureWrapMode.Clamp;

        pixels = new Color32[width * height];

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, width, height),
            Vector2.zero,
            1f);

        SpriteRenderer rend = GetComponent<SpriteRenderer>();
        if (rend == null) rend = gameObject.AddComponent<SpriteRenderer>();

        // Usar material del prefab si existe para compatibilidad total con URP 2D
        if (cellPrefab != null)
        {
            SpriteRenderer prefabRend = cellPrefab.GetComponent<SpriteRenderer>();
            if (prefabRend != null && prefabRend.sharedMaterial != null)
            {
                rend.sharedMaterial = prefabRend.sharedMaterial;
            }
        }
        else
        {
            Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit");
            if (s == null) s = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s != null) rend.material = new Material(s);
        }

        rend.sprite = sprite;
        rend.sortingOrder = 0;
    }

    // Vacia toda la grilla y reinicia contadores
    public void ClearGrid()
    {
        dia = 0;
        arenaTotal = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = false;
            }
        }
        UpdateVisuals();
    }

    // Siembra inicial de arena en la parte superior para observar la caida y monticulos
    void RandomizeGrid()
    {
        dia = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Aparece arena aleatoria en la mitad superior de la grilla
                grid[x, y] = (y > height / 2) && (Random.value > 0.6f);
            }
        }
        UpdateVisuals();
    }

    // Logica del automata: aplica las reglas de Falling Sand en cada generacion
    void Step()
    {
        dia++; // Incremento del dia / generacion

        // Copiar estado actual al buffer de destino
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                nextGrid[x, y] = grid[x, y];
            }
        }

        int movingCount = 0;
        int restingCount = 0;
        arenaTotal = 0;

        // Recorrido de abajo hacia arriba para que cada particula avance maximo 1 celda por tick
        for (int y = 0; y < height; y++)
        {
            // Alternar direccion horizontal para evitar sesgos laterales
            bool leftToRight = Random.value > 0.5f;

            for (int i = 0; i < width; i++)
            {
                int x = leftToRight ? i : (width - 1 - i);

                // Solo se procesan celdas que contienen arena
                if (!grid[x, y]) continue;

                arenaTotal++;

                // Si esta en el fondo (y = 0), no puede caer mas
                if (y == 0)
                {
                    restingCount++;
                    continue;
                }

                // 1. Caida vertical: si la celda inmediatamente debajo esta vacia
                if (!nextGrid[x, y - 1])
                {
                    nextGrid[x, y] = false;
                    nextGrid[x, y - 1] = true;
                    movingCount++;
                }
                else
                {
                    // 2. Colision con otra particula: intentar movimiento diagonal
                    bool leftFree = (x > 0) && !nextGrid[x - 1, y - 1];
                    bool rightFree = (x < width - 1) && !nextGrid[x + 1, y - 1];

                    if (leftFree && rightFree)
                    {
                        // Si ambas diagonales estan libres, escoger aleatoriamente (50/50)
                        int targetX = (Random.value < 0.5f) ? (x - 1) : (x + 1);
                        nextGrid[x, y] = false;
                        nextGrid[targetX, y - 1] = true;
                        movingCount++;
                    }
                    else if (leftFree)
                    {
                        // Si solo abajo-izquierda esta libre
                        nextGrid[x, y] = false;
                        nextGrid[x - 1, y - 1] = true;
                        movingCount++;
                    }
                    else if (rightFree)
                    {
                        // Si solo abajo-derecha esta libre
                        nextGrid[x, y] = false;
                        nextGrid[x + 1, y - 1] = true;
                        movingCount++;
                    }
                    else
                    {
                        // 3. Bloqueo: si abajo y ambas diagonales estan ocupadas, permanece en reposo
                        restingCount++;
                    }
                }
            }
        }

        // Intercambio de buffers
        var temp = grid;
        grid = nextGrid;
        nextGrid = temp;

        // Mostrar en consola la evolucion dia a dia
        Debug.Log($"[Dia {dia}] Particulas de arena: {arenaTotal} | En movimiento: {movingCount} | En reposo: {restingCount}");
    }

    void HandleMouseClick(bool paintSand)
    {
        if (Camera.main == null) return;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        ToggleCellAtWorld(worldPos, paintSand);
    }

    // Aplica arena en la posicion del mundo seleccionada
    void ToggleCellAtWorld(Vector3 worldPos, bool forceSand)
    {
        int x = Mathf.FloorToInt(worldPos.x);
        int y = Mathf.FloorToInt(worldPos.y);

        if (x < 0 || x >= width || y < 0 || y >= height) return;

        grid[x, y] = forceSand ? true : !grid[x, y];
        UpdateVisuals();
    }

    // Actualiza la textura visual en pantalla
    void UpdateVisuals()
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                pixels[row + x] = grid[x, y] ? SandColor : EmptyColor;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
    }

    // Muestra en pantalla la evolucion dia a dia
    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 270, 115), "Simulacion de Arena (Falling Sand)");
        GUI.Label(new Rect(20, 35, 250, 20), $"Dia / Generacion: {dia}");
        GUI.Label(new Rect(20, 55, 250, 20), $"Particulas de arena: {arenaTotal}");
        GUI.Label(new Rect(20, 75, 250, 20), $"Estado: {(isPaused ? "Pausado [P]" : "Simulando...")}");
        GUI.Label(new Rect(20, 95, 250, 20), "Controles: [P] Pausa | [R] Reiniciar | [E] Limpiar");
    }

    // Dibuja el recuadro de la grilla en la vista de escena de Unity
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.75f, 0.3f, 0.8f);
        Gizmos.DrawWireCube(new Vector3(width / 2f, height / 2f, 0), new Vector3(width, height, 0.1f));
    }
}

/*
 * Nota de autoria y revision:
 * Codigo adaptado de la simulacion del Juego de la Vida a Falling Sand.
 * Ajustes, correcciones y revision realizados con la asistencia de Gemini.
 */
