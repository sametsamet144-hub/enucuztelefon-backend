# EnUcuzTelefon — Backend Hizmetleri (.NET 8 & Dağıtık Mimari)

EnUcuzTelefon platformunun REST API, Fiyat Doğrulama, Eşleştirme Motoru, Arka Plan Mağaza Tarayıcı (Worker) ve Quartz Zamanlayıcı (Scheduler) altyapısı.

## 🏛️ Mimari (Clean Architecture & Microservices)

- **`EnUcuzTelefon.Api`**: RESTful Web API, Swagger, JWT Auth, CORS, Health Checks
- **`EnUcuzTelefon.Application`**: CQRS / İş Mantığı, Servisler, DTO'lar, Arayüzler
- **`EnUcuzTelefon.Domain`**: Varlıklar (Entities), Değer Nesneleri, Enum'lar
- **`EnUcuzTelefon.Infrastructure`**: EF Core (PostgreSQL), Redis Cache, RabbitMQ Bus, Mağaza Adaptörleri (Trendyol, Hepsiburada, n11)
- **`EnUcuzTelefon.Worker`**: Arka plan fiyat tarama ve doğrulama motoru
- **`EnUcuzTelefon.Scheduler`**: Quartz.NET 2 saatlik periyodik tarama zamanlayıcısı
- **`EnUcuzTelefon.UnitTests` & `ArchitectureTests`**: Kapsamlı birim ve mimari testleri

## 🚀 Docker Compose ile Çalıştırma

Tüm backend altyapısını (PostgreSQL 16, Redis 7, RabbitMQ 3.13, API, Worker, Scheduler) tek komutla başlatabilirsiniz:

```bash
cp .env.example .env
docker compose up -d --build
```

- **REST API & Swagger**: `http://localhost:5000` (`http://localhost:5000/swagger`)
- **RabbitMQ Management**: `http://localhost:15672` (guest/guest)
- **PostgreSQL**: `localhost:5432`

## 🛠️ Yerel Geliştirme (Local Development)

```bash
# Bağımlılıkları derleyin
dotnet build EnUcuzTelefon.sln

# Testleri çalıştırın
dotnet test

# API'yi başlatın
dotnet run --project src/EnUcuzTelefon.Api
```

## ☁️ Bulut & PaaS Dağıtımı (Railway, Render, Fly.io, VPS)
- `railway.toml` ve `deploy/docker/` klasöründeki Dockerfile'lar ile tek tıkla canlıya alınabilir.
