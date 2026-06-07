# КиноБездна — To-Be Архитектура

## Задание 1 — Контейнерная диаграмма (C4 Container)

### Описание доменов

| Домен | Сервис | Ответственность |
|---|---|---|
| **Шлюз** | API Gateway (Proxy) | Единая точка входа, маршрутизация, feature flags, rate limiting |
| **Пользователи** | User Service | Регистрация, аутентификация (JWT), управление профилем |
| **Фильмы** | Movies Service | Каталог фильмов, метаданные, жанры, рейтинги |
| **Подписки** | Subscription Service | Тарифные планы, управление подписками, доступ к контенту |
| **Платежи** | Payment Service | Обработка платежей, история транзакций |
| **События** | Events Service | Kafka producer/consumer, аналитика действий пользователей |
| **Уведомления** | Notification Service | Email/push-уведомления по событиям из Kafka |

### Интеграционное взаимодействие

- **Синхронное (HTTP/REST)** — API Gateway → сервисы (запрос-ответ)
- **Асинхронное (Kafka)** — сервисы публикуют доменные события; Events Service и Notification Service подписываются на топики
- **Кэш (Redis)** — API Gateway кэширует сессии и горячие данные

---

### C4 Container Diagram — КиноБездна (To-Be)

```mermaid
C4Container
    title КиноБездна — Контейнерная диаграмма (To-Be)

    Person(viewer, "Зритель", "Пользователь платформы КиноБездна")
    Person(admin, "Администратор", "Управляет контентом и конфигурацией")

    System_Boundary(cinemaabyss, "КиноБездна") {

        Container(api_gw, "API Gateway", "Go · Proxy Service", "Единая точка входа.\nМаршрутизация запросов, JWT-валидация,\nfeature flags, rate limiting,\nпостепенная миграция трафика (Strangler Fig)")

        Container(redis, "Session Cache", "Redis", "Кэш JWT-сессий и горячих данных")

        Container(user_svc, "User Service", "Go", "Регистрация и аутентификация\nпользователей, управление профилем")
        Container(movies_svc, "Movies Service", "Go", "Каталог фильмов, метаданные,\nжанры, рейтинги, поиск")
        Container(subscription_svc, "Subscription Service", "Go", "Тарифные планы, управление\nподписками, проверка доступа")
        Container(payment_svc, "Payment Service", "Go", "Обработка платежей,\nистория транзакций, возвраты")
        Container(events_svc, "Events Service", "Go", "Kafka producer + consumer.\nАналитика событий: просмотры,\nоценки, входы, платежи")
        Container(notification_svc, "Notification Service", "Go", "Отправка email и push-уведомлений\nпо событиям из Kafka")

        ContainerDb(user_db, "Users DB", "PostgreSQL", "Пользователи, учётные данные")
        ContainerDb(movies_db, "Movies DB", "PostgreSQL", "Фильмы, жанры, рейтинги")
        ContainerDb(subscription_db, "Subscriptions DB", "PostgreSQL", "Подписки и тарифные планы")
        ContainerDb(payment_db, "Payments DB", "PostgreSQL", "Транзакции и платёжная история")
        ContainerDb(events_db, "Events DB", "PostgreSQL", "Лог доменных событий")

        Container(kafka, "Message Bus", "Apache Kafka", "Асинхронная шина событий.\nТопики: user-events, movie-events,\npayment-events, subscription-events")
    }

    System_Ext(payment_gw, "Payment Gateway", "Внешний провайдер платежей\n(Stripe / ЮKassa)")
    System_Ext(email_provider, "Email Provider", "Внешний сервис отправки\nтранзакционных email")

    Rel(viewer, api_gw, "HTTPS / REST", "Browser / Mobile App")
    Rel(admin, api_gw, "HTTPS / REST", "Admin Panel")

    Rel(api_gw, redis, "Redis Protocol", "Кэш сессий")
    Rel(api_gw, user_svc, "HTTP / REST", "/api/users, /api/auth")
    Rel(api_gw, movies_svc, "HTTP / REST", "/api/movies")
    Rel(api_gw, subscription_svc, "HTTP / REST", "/api/subscriptions")
    Rel(api_gw, payment_svc, "HTTP / REST", "/api/payments")
    Rel(api_gw, events_svc, "HTTP / REST", "/api/events")

    Rel(user_svc, user_db, "SQL / TCP")
    Rel(movies_svc, movies_db, "SQL / TCP")
    Rel(subscription_svc, subscription_db, "SQL / TCP")
    Rel(payment_svc, payment_db, "SQL / TCP")
    Rel(events_svc, events_db, "SQL / TCP")

    Rel(user_svc, kafka, "Produce", "user-events")
    Rel(movies_svc, kafka, "Produce", "movie-events")
    Rel(payment_svc, kafka, "Produce", "payment-events")
    Rel(subscription_svc, kafka, "Produce", "subscription-events")

    Rel(events_svc, kafka, "Consume", "все топики")
    Rel(notification_svc, kafka, "Consume", "user-events, payment-events")

    Rel(payment_svc, payment_gw, "HTTPS / REST", "Проведение транзакций")
    Rel(notification_svc, email_provider, "SMTP / API", "Отправка email")
```

---

### Ключевые архитектурные решения

1. **Database per Service** — каждый сервис имеет собственную схему БД, исключая прямые межсервисные JOIN-запросы.
2. **Strangler Fig** — API Gateway постепенно переключает трафик от монолита к микросервисам через feature flag `MOVIES_MIGRATION_PERCENT`.
3. **Event-Driven Integration** — доменные события публикуются в Kafka; потребители реагируют асинхронно, снижая связность сервисов.
4. **Single Entry Point** — весь внешний трафик проходит через API Gateway, который отвечает за аутентификацию, rate limiting и маршрутизацию.
