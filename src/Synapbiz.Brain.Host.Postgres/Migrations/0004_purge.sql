-- The indexes of the purge commands (W1-F). Additive: no table or column changes, and it applies on a database that holds data.

-- PurgeFinishedBeforeAsync: the finished roots of a tenant, oldest update first, ties broken by the instance id in byte order. The index
-- holds only finished roots, so it stays small however many running and child instances there are.
create index ix_process_instance_purge on process_instance (tenant_id, updated_at, instance_id collate "C") where parent_instance_id is null and status in (4, 5);

-- PurgeEventsBeforeAsync: the event ids of a tenant, oldest first, ties broken by the event id in byte order.
create index ix_process_event_received on process_event (tenant_id, received_at, event_id collate "C");

-- PurgeInstanceAsync deletes the inbox rows of a tree in every status. The existing index on the inbox is partial (pending rows only), so
-- it cannot serve that delete, which would scan the whole table once per tree.
create index ix_process_inbox_instance on process_inbox (tenant_id, instance_id);
