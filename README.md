# DeepResearchAgent

**DeepResearchAgent**, kullanıcıların belirli bir konu hakkında kapsamlı araştırma yapmasına yardımcı olmak amacıyla geliştirilmiş, .NET tabanlı bir yapay zekâ destekli araştırma uygulamasıdır.

Uygulamanın temel amacı, verilen araştırma konusuna ilişkin web kaynaklarını toplamak, içerikleri analiz etmek ve elde edilen bilgileri kaynak bağlantılarıyla birlikte düzenli bir araştırma raporuna dönüştürmektir.

## Proje Hakkında

Geleneksel araştırma süreçlerinde farklı web sitelerini ziyaret etmek, ilgili bilgileri ayıklamak, kaynakları incelemek ve bulguları raporlamak zaman alır.

DeepResearchAgent, bu sürecin çeşitli aşamalarını otomatikleştirmek için geliştirilmiştir.

Örnek araştırma konusu:

> Yapay zekânın tekstil sektörüne etkisi

Uygulamanın hedeflenen araştırma akışı; araştırma isteğinin alınması, ilgili web kaynaklarının bulunması, kaynak içeriklerinin toplanması, yapay zekâ ile analiz edilmesi ve araştırma raporunun oluşturulması adımlarından oluşur.

## Temel Özellikler

- **Araştırma yönetimi:** Araştırma taleplerinin ve süreç durumlarının yönetilmesi.
- **Web araması:** Araştırma konusuyla ilgili kaynakların bulunması.
- **Web tarama:** İlgili sayfalardan içerik toplanması.
- **Yapay zekâ destekli analiz:** Toplanan içeriklerden anlamlı bilgilerin çıkarılması.
- **Otomatik raporlama:** Araştırma bulgularının düzenli bir rapora dönüştürülmesi.
- **Web arayüzü:** Araştırma sürecinin başlatılması ve oluşturulan raporların görüntülenmesi.

*Not: Özelliklerin kullanılabilirliği, projenin son kod sürümündeki uygulamaya bağlıdır.*

## Kullanılan Teknolojiler

### Backend
- C#
- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- Microsoft SQL Server
- Dependency Injection
- Background Services / Worker Service

### Yapay Zekâ ve Araştırma
- NVIDIA AI API
- Tavily Search API
- Web crawling
- Qdrant vektör veritabanı
- RAG (Retrieval-Augmented Generation) altyapısı

### Frontend
- React
- TypeScript
- Vite

### Diğer Araçlar
- Git ve GitHub
- REST API
- JSON tabanlı veri alışverişi

## Mimari

Backend, sorumlulukları farklı katmanlara ayıran katmanlı bir mimariyle tasarlanmıştır.

| Katman | Sorumluluk |
|---|---|
| `DeepResearchAgent.Core` | Temel varlıklar, enum'lar ve çekirdek modeller |
| `DeepResearchAgent.Engine` | Araştırma iş akışı, servisler ve uygulama arayüzleri |
| `DeepResearchAgent.Infrastructure` | Veritabanı erişimi, dış servis entegrasyonları ve teknik uygulamalar |
| `DeepResearchAgent.WebAPI` | HTTP API uç noktaları ve web uygulaması entegrasyonları |
| `DeepResearchAgent.Worker` | Arka plan araştırma işlemlerinin yürütülmesi |
| `DeepResearchAgent.Tests` | Test işlemleri |

Frontend, backend API'sinden ayrı bir proje olarak geliştirilmiştir.

## Araştırma Akışı

1. Kullanıcı araştırma konusunu belirler.
2. Araştırma kaydı oluşturulur.
3. Araştırma konusu için web araması gerçekleştirilir.
4. İlgili kaynakların içerikleri toplanır.
5. Toplanan içerikler analiz edilir.
6. Araştırma bulguları rapor hâline getirilir.
7. Oluşturulan rapor kullanıcıya sunulur.

## Veritabanı

Projenin veri katmanında Microsoft SQL Server ve Entity Framework Core kullanılmıştır.

Araştırma kayıtları, kaynaklar ve raporlar gibi veriler ilişkisel veritabanında saklanacak şekilde tasarlanmıştır. RAG altyapısında ise araştırma içeriklerinin vektör temsillerini saklamak ve ilgili içerikleri geri getirmek amacıyla Qdrant kullanılmıştır.

## Kurulum

### Gereksinimler

- .NET 10 SDK
- Node.js ve npm
- Microsoft SQL Server
- NVIDIA API erişimi
- Tavily API anahtarı
- RAG bileşenleri kullanılacaksa Qdrant

### 1. Repoyu klonlayın

```bash
git clone <repository-url>
cd DeepResearchAgent
```

`<repository-url>` alanını kendi GitHub depo adresinizle değiştirin.

### 2. Yapılandırmayı ayarlayın

Backend'in yapılandırma dosyalarında veya User Secrets üzerinden veritabanı bağlantı bilgisini ve gerekli API anahtarlarını tanımlayın.

Örnek yapılandırma alanları:

- SQL Server connection string
- NVIDIA API anahtarı ve model ayarları
- Tavily API anahtarı
- Qdrant adresi

API anahtarlarını herkese açık depolara yüklemeyin.

### 3. Veritabanını hazırlayın

Projedeki mevcut Entity Framework Core migration'larını kontrol edin ve veritabanını bu migration'larla eşitleyin.

```bash
dotnet ef database update
```

Bu komutu, uygun DbContext ve migration projesi yapılandırılmışken çalıştırın.

### 4. Backend'i çalıştırın

Visual Studio üzerinden `DeepResearchAgent.WebAPI` ve `DeepResearchAgent.Worker` projelerini, yapılandırmalarına uygun şekilde başlatın.

Worker, araştırma işlemlerinin arka planda yürütülmesi için kullanılır.

### 5. Frontend'i çalıştırın

Frontend proje klasörüne geçin:

```bash
cd ../DeepResearchAgent-Frontend
npm install
npm run dev
```

Frontend'in API adresinin ve backend CORS ayarlarının yerel geliştirme ortamınızla eşleştiğinden emin olun.

> Kurulum komutları ve servis adresleri, projedeki son yapılandırmaya göre uyarlanmalıdır.

## Proje Hedefi

DeepResearchAgent'ın hedefi yalnızca bir soru-cevap uygulaması oluşturmak değil; web araştırması, içerik toplama, yapay zekâ destekli analiz raporlama adımlarını bir araya getiren bir araştırma sistemi geliştirmektir.

## Öğrenme Kazanımları

Proje üzerinde çalışırken aşağıdaki yazılım geliştirme konularını uygulama fırsatı elde edilmiştir:

- .NET ile katmanlı backend mimarisi geliştirme
- ASP.NET Core Web API tasarımı
- Entity Framework Core ve SQL Server ile veri yönetimi
- Harici API'lerle entegrasyon
- Background Worker kullanımı
- Web crawling ve arama servisleriyle çalışma
- Yapay zekâ API'lerini backend süreçlerine entegre etme
- Vektör veritabanı ve RAG yaklaşımıyla çalışma
- React ve TypeScript ile frontend geliştirme
- Backend ve frontend arasındaki API iletişimi
- Git ile sürüm kontrolü

## Lisans

Bu proje için lisans belirtilmemiştir. Açık kaynak olarak paylaşılacaksa uygun bir lisans eklenmelidir.
