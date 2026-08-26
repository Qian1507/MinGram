# MinGram — Säkra din bildtjänst i Azure

> Vecka 35 · Du har appen · Nu är det dags att säkra den

Du byggde MinGram förra veckan. Grattis. Nu är det ingen som ska komma åt den utan tillstånd.

Den här inlämningen handlar **inte** om hur du kodar. Den handlar om hur du tänker kring säkerhet — vem som får göra vad, i vilket nätverk, med vilka rättigheter.

---

## Koden — repetition, inte uppgiften

Du får ett färdigt API-startprojekt (`starter-mingram-api/`). Deploya det till Azure App Service.

API:n har dessa endpoints klara att testa i Swagger:

```
GET    /bilder           → lista alla bilder
GET    /bilder/{id}      → hämta en specifik bild
POST   /bilder           → lägg till en bild (skicka URL, caption och taggar)
PUT    /bilder/{id}      → uppdatera caption eller taggar
DELETE /bilder/{id}      → ta bort en bild
```

Koden är klar. Det är repetition — och det är meningen.  
**Du bedöms inte på koden. Du bedöms på vad du gör i Azure.**

Vill du ha ett gränssnitt kan du använda Blazor-startern (`starter-mingram/`) eller bygga i React.  
Det är helt valfritt — Postman räcker för att bevisa att Azure-konfigurationen fungerar.

---

## Det här är uppgiften — Azure-säkerhetskonfigurationen

MinGram körs nu av ett litet startup. De har tre typer av användare:

| Roll | Vad de får göra |
|------|----------------|
| **Admin** | Allt — ladda upp, ta bort, läsa, hantera tjänster |
| **Fotograf** | Ladda upp och läsa bilder |
| **Betraktare** | Endast läsa bilder |

Ditt jobb är att bygga upp Azure-miljön så att dessa rättigheter faktiskt gäller — inte bara i teorin.

### 1. Nätverk (VNet + NSG)

Skapa ett virtuellt nätverk med minst **två subnets**:
- `frontend-subnet` — där appen lever
- `backend-subnet` — där lagringen kommunicerar internt

Sätt upp NSG-regler som följer **deny by default**:
- Port 443 (HTTPS) in → tillåten från internet till frontend
- Port 80 (HTTP) in → blockerad (tvinga HTTPS)
- All annan inkommande trafik → blockerad
- Intern kommunikation mellan subnets → tillåten

### 2. Identitet och roller (Entra ID)

Skapa tre användare i Entra ID:
- `admin@[dittnamn].onmicrosoft.com`
- `fotograf@[dittnamn].onmicrosoft.com`
- `betraktare@[dittnamn].onmicrosoft.com`

**Två typer av roller — viktigt att hålla isär:**

| Typ | Var | Styr |
|-----|-----|------|
| **App-roller** (Entra ID) | App registrations → App roles | Vem som får anropa vilka API-endpoints |
| **Azure RBAC** | Storage Account | Vem som får läsa och skriva till Blob Storage |

App-rollerna (`Betraktare`, `Fotograf`, `Admin`) skapas och tilldelas i steg 3.

Azure RBAC på Storage Account är ett parallellt lager för direktåtkomst till lagringen:
- `Fotograf` → **Storage Blob Data Contributor** (kan ladda upp bilder)
- `Betraktare` → **Storage Blob Data Reader** (kan bara läsa)
- `Admin` → **Storage Blob Data Owner**

Det innebär att även om någon kringgår API:n och försöker nå Blob Storage direkt — nekas de av Azure.

### 3. Autentisering (Easy Auth)

Aktivera App Service Authentication i Azure Portal så att alla anrop till API:n kräver inloggning med Entra ID:

1. Gå till din API-App Service → **Authentication** → **Add identity provider**
2. Välj **Microsoft**, välj din Entra ID-tenant
3. Sätt "Unauthenticated requests" till **HTTP 401**
4. Gå till **App registrations** → din app → **App roles** och skapa rollerna:
   `Betraktare`, `Fotograf`, `Admin`
5. Gå till **Enterprise applications** → din app → **Users and groups**
   och tilldela rätt roll till varje Entra ID-användare

API:n läser rollen ur den token Azure skickar — rätt roll → rätt åtkomst.

### 4. CORS

När frontend och API körs på olika Azure-domäner blockerar webbläsaren anropen — det kallas CORS (Cross-Origin Resource Sharing).

1. Gå till din API-App Service → **API** → **CORS**
2. Lägg till din frontend-URL (t.ex. `https://mingram-ui.azurewebsites.net`)
3. Spara och verifiera att anropen fungerar

### 5. Bevisa att det fungerar

Logga in som `betraktare` i Postman (hämta token via OAuth) och försök ta bort en bild.  
Det ska ge **403 Forbidden** — ta en skärmdump, det är ditt bevis.

---

## Om startprojekten

Du deploya API:n (`starter-mingram-api/`) till Azure App Service — det är det som bedöms.

UI är valfritt. Blazor-startern (`starter-mingram/`) finns om du vill ha ett gränssnitt, men du kan lika gärna bygga i React eller bara använda Postman. Det spelar ingen roll — det är Azure-konfigurationen som bedöms.

---

## Krav för G

- [ ] MinGram är deployad till Azure App Service
- [ ] VNet med två subnets är skapat
- [ ] NSG blockerar port 80 och allt annat utom 443
- [ ] Tre Entra ID-användare är skapade
- [ ] RBAC-roller är tilldelade korrekt
- [ ] Easy Auth aktiverat — anrop utan inloggning ger 401
- [ ] App-roller skapade i Entra ID och tilldelade till användarna
- [ ] CORS konfigurerat på API:ts App Service med frontend-URL som tillåten origin
- [ ] Skärmdump som visar att `betraktare` får 403 vid DELETE

> **Skolkonto-notering:** Vissa delar av uppgiften kanske inte går att genomföra fullt ut med skolans Azure-konto — t.ex. kan skapande av resursgrupper eller vissa RBAC-tilldelningar vara låsta av administratörspolicyer. Om du stöter på det: **dokumentera i din README** vad du inte kunde göra, hur det *hade* gjorts om du haft rätt behörighet, och varför det är relevant. Det räknas som fullgjort krav.

## Krav för VG (utöver G)

- [ ] Du motiverar dina NSG-regler i rapporten — varför just dessa portar, vad skulle hända utan dem
- [ ] Du förklarar varför du valt de specifika inbyggda rollerna (eller varför du skapat en custom roll)
- [ ] Du resonerar kring vad som saknas i den här miljön jämfört med produktion — vad skulle nästa steg vara?
- [ ] **Individuell reflektion:** Varje gruppmedlem skriver kort om sina egna tankar kring de Azure-delar gruppen jobbade med — vad upplevde du som krångligt, vad är nyttan med det, vad skulle du ha gjort annorlunda om du fick göra om det? Ska vara personlig och inte kopierad från gruppen.

---

## Rapport

Lämna in en kort rapport (Markdown eller PDF, max 2 sidor):

1. **Nätverksarkitektur** — en enkel skiss (text-diagram fungerar) över hur subnets och NSG hänger ihop
2. **Rollmotivering** — varför valde du de RBAC-roller du valde?
3. **Bevis** — skärmdump på nekad åtkomst

Koden lämnar du in som en länk till ditt GitHub-repo.

---

## Inlämning

- Deadline: **söndag 30 aug midnatt**
- Presentation: **tisdag 1 sep förmiddag**
- Lämna in via Google Classroom
- Repo-länk + rapport i samma inlämning
