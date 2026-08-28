import { useEffect, useState } from "react";
import "./App.css";

type Bild = {
  id: number;
  namn: string;
  caption: string;
  taggar: string[];
  url: string;
};

const API_URL = import.meta.env.VITE_API_URL;


function App() {
  const [bilder, setBilder] = useState<Bild[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [caption, setCaption] = useState("");
  const [taggar, setTaggar] = useState("");
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(false);

  async function hamtaBilder() {
    try {
      const response = await fetch(`${API_URL}/bilder`);

      if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}`);
      }

      const data: Bild[] = await response.json();
      setBilder(data);
    } catch (error) {
      console.error(error);
      setMessage("Kunde inte hämta bilder.");
    }
  }

  useEffect(() => {
  async function loadBilder() {
    try {
      const response = await fetch(`${API_URL}/bilder`);

      if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}`);
      }

      const data: Bild[] = await response.json();
      setBilder(data);
    } catch (error) {
      console.error(error);
      setMessage("Kunde inte hämta bilder.");
    }
  }

  void loadBilder();
}, []);

  async function laddaUpp() {
    if (!file) {
      setMessage("Välj en bild först.");
      return;
    }

    setLoading(true);
    setMessage("");

    try {
      const formData = new FormData();

      formData.append("file", file);
      formData.append("caption", caption);
      formData.append("taggar", taggar);

      const response = await fetch(`${API_URL}/bilder`, {
        method: "POST",
        body: formData,
      });

      if (!response.ok) {
        const text = await response.text();
        throw new Error(
          `${response.status} ${response.statusText}: ${text}`
        );
      }

      setMessage("Bilden laddades upp!");
      setFile(null);
      setCaption("");
      setTaggar("");

      const fileInput = document.getElementById(
        "fileInput"
      ) as HTMLInputElement;

      if (fileInput) {
        fileInput.value = "";
      }

      await hamtaBilder();
    } catch (error) {
      console.error(error);

      if (error instanceof Error) {
        setMessage(`Uppladdningen misslyckades: ${error.message}`);
      } else {
        setMessage("Uppladdningen misslyckades.");
      }
    } finally {
      setLoading(false);
    }
  }

  async function raderaBild(id: number) {
    try {
      const response = await fetch(`${API_URL}/bilder/${id}`, {
        method: "DELETE",
      });

      if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}`);
      }

      setMessage("Bilden togs bort.");
      await hamtaBilder();
    } catch (error) {
      console.error(error);

      if (error instanceof Error) {
        setMessage(`Kunde inte ta bort bilden: ${error.message}`);
      }
    }
  }

  return (
    <main className="container">
  <h1>MinGram</h1>

  <section>
    <h2>Bilder</h2>

    {bilder.length === 0 ? (
      <p>Inga bilder ännu. Ladda upp den första!</p>
    ) : (
      <div className="gallery">
        {bilder.map((bild) => (
          <article key={bild.id} className="card">
            <img
              src={bild.url}
              alt={bild.caption || bild.namn}
            />

            <div className="cardContent">
              <strong>{bild.caption}</strong>

              <p>
                {bild.taggar
                  .map((tagg) => `#${tagg}`)
                  .join(" · ")}
              </p>

              <button
                className="deleteButton"
                onClick={() => raderaBild(bild.id)}
              >
                Ta bort
              </button>
            </div>
          </article>
        ))}
      </div>
    )}
  </section>

  <hr />

  <section>
    <h2>Ladda upp bild</h2>

    <div className="upload">
      <label>
        Bild
        <input
          id="fileInput"
          type="file"
          accept="image/*"
          onChange={(e) => {
            setFile(e.target.files?.[0] ?? null);
          }}
        />
      </label>

      <label>
        Caption
        <input
          type="text"
          value={caption}
          onChange={(e) => setCaption(e.target.value)}
          placeholder="Beskriv bilden..."
        />
      </label>

      <label>
        Taggar (kommaseparerade)
        <input
          type="text"
          value={taggar}
          onChange={(e) => setTaggar(e.target.value)}
          placeholder="semester, strand, sol"
        />
      </label>

      <button
        onClick={laddaUpp}
        disabled={!file || loading}
      >
        {loading ? "Laddar upp..." : "Ladda upp"}
      </button>

      {message && (
        <p className="message">{message}</p>
      )}
    </div>
  </section>
</main>
  );
}

export default App;