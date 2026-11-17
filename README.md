# Microservices Event-Driven Demo (1 CPU, 1GB RAM Optimized)

A lightweight .NET 10 microservices architecture utilizing **RabbitMQ**, **MySQL (MariaDB)**, **Docker Compose**, **Kubernetes (k3s)**, and **GitHub Actions CI/CD**.

---

## 🏗️ Architecture & Fault Tolerance

```
[Client]
   │ POST /api/orders
   ▼
[Order Service] ──(Save: PENDING)──> [order_db]
   │
   │ (Publish: OrderCreatedEvent)
   ▼
[RabbitMQ: order.created.queue]
   │
   │ (Consume)
   ▼
[Payment Service] ──(Deduct Balance)──> [payment_db]
   │
   │ (Publish: PaymentCompletedEvent)
   ▼
[RabbitMQ: payment.completed.queue]
   │
   │ (Consume)
   ▼
[Order Service] ──(Update: COMPLETED / FAILED)──> [order_db]
```

### ⚡ Resilience Test (When Payment Service is Down):
1. Stop Payment Service: `docker stop payment-service`.
2. Post a new order to `http://localhost:5001/api/orders`.
3. The order is stored as `PENDING`, and the message is safely queued inside RabbitMQ without crashing the system.
4. Start Payment Service: `docker start payment-service`.
5. Payment Service immediately consumes the pending message, processes payment, and Order Service finalizes the order to `COMPLETED`.

---

## 🚀 How to Run Locally with Docker Compose

1. **Start all services**:
   ```bash
   docker compose up --build -d
   ```

2. **Verify containers**:
   ```bash
   docker compose ps
   ```

3. **Check RabbitMQ UI**:
   Open browser at `http://localhost:15672` (User: `guest` / Pass: `guest`).

---

## 🧪 Testing the APIs

### 1. Check Customer Wallet Balance
Default seeded customer is `cust_001` with `$100.00` balance:
```bash
curl http://localhost:5002/api/wallets/cust_001
```

### 2. Create an Order (Success Case)
```bash
curl -X POST http://localhost:5001/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId": "cust_001", "totalAmount": 25.50}'
```
Response:
```json
{
  "orderId": "your-order-guid",
  "status": "PENDING",
  "message": "Order accepted and is currently PENDING payment processing."
}
```

### 3. Verify Order Status Updated to COMPLETED
```bash
curl http://localhost:5001/api/orders/{orderId}
```
Status will transition from `PENDING` to `COMPLETED`, and customer wallet balance will be `$74.50`.

### 4. Test Insufficient Funds (Failure Case)
Place an order exceeding balance:
```bash
curl -X POST http://localhost:5001/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId": "cust_001", "totalAmount": 5000.00}'
```
Checking the order status will show `FAILED`.

---

## 🖥️ 1GB RAM Server Setup Guide

1. **Enable Swap Memory (Essential for 1GB RAM)**:
   ```bash
   sudo fallocate -l 3G /swapfile
   sudo chmod 600 /swapfile
   sudo mkswap /swapfile
   sudo swapon /swapfile
   echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
   ```

2. **Deploy via k3s (Lightweight Kubernetes)**:
   ```bash
   curl -sfL https://get.k3s.io | INSTALL_K3S_EXEC="--disable traefik --disable servicelb --disable metrics-server" sh -
   kubectl apply -f k8s/app-deployments.yaml
   ```
