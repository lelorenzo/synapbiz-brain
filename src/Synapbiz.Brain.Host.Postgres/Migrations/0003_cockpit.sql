-- The indexes of the cockpit queries (W1-E). Additive: no table or column changes, and it applies on a database that holds data.

-- The order of the instance list: newest first, ties broken by the instance id in byte order (the queries compare and sort it
-- with collate "C", whatever the locale of the database), within one tenant.
create index ix_process_instance_created on process_instance (tenant_id, created_at desc, instance_id collate "C" desc);

-- Filtering and counting by status within one tenant.
create index ix_process_instance_status on process_instance (tenant_id, status);
