import { api } from './client.js';

export const productApi = {
  list: (params = {}) => {
    const q = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => { if (v !== null && v !== undefined && v !== '') q.set(k, v); });
    return api.get(`/products?${q.toString()}`);
  },
  detail:     (id)    => api.get(`/products/${id}`, true),
  categories: ()      => api.get('/products/categories'),
  addReview:  (id, data) => api.post(`/products/${id}/reviews`, data, true),
};
